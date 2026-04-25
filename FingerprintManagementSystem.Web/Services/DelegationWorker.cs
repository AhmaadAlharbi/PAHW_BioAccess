using FingerprintManagementSystem.ApiAdapter.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using FingerprintManagementSystem.Contracts;

namespace FingerprintManagementSystem.Web.Services;

public class DelegationWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DelegationWorker> _logger;

    public DelegationWorker(IServiceScopeFactory scopeFactory, ILogger<DelegationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<LocalAppDbContext>();
                var api = scope.ServiceProvider.GetRequiredService<IEmployeeDevicesApi>();
                var activity = scope.ServiceProvider.GetRequiredService<IActivityLogService>();

                var now = DateTime.Now;

                // 1) التحقق من الانتدابات الجاهزة للبدء
                var toStart = await db.Delegations
                    .Where(x => x.Status == "Scheduled" && x.StartDate <= now)
                    .ToListAsync(stoppingToken);

                foreach (var del in toStart)
                {
                    var terminals = await db.DelegationTerminals
                        .Where(t => t.DelegationId == del.Id)
                        .Select(t => t.TerminalId)
                        .ToListAsync(stoppingToken);

                    foreach (var terminalId in terminals)
                    {
                        // نحاول 3 مرات قبل الفشل
                        var success = false;
                        for (var i = 1; i <= 3 && !success; i++)
                        {
                            success = await api.AssignOneAsync(del.EmployeeId, terminalId, stoppingToken);
                            if (!success) await Task.Delay(500, stoppingToken);
                        }
                    }

                    del.Status = "Active";
                    del.ActivatedAt = now;

                    await activity.LogSystemAsync(
                        actorName: "DelegationWorker",
                        action: "Delegation.Activated",
                        entityType: "Delegation",
                        entityId: del.Id.ToString(),
                        summary: $"تم تفعيل انتداب للموظف {del.EmployeeId} ({terminals.Count} أجهزة).",
                        details: new { delegationId = del.Id, employeeId = del.EmployeeId, terminalCount = terminals.Count },
                        ct: stoppingToken
                    );
                }

                // 2) التحقق من الانتدابات المنتهية
                var toExpire = await db.Delegations
                    .Where(x => x.Status == "Active" && x.EndDate <= now)
                    .ToListAsync(stoppingToken);

                foreach (var del in toExpire)
                {
                    var terminals = await db.DelegationTerminals
                        .Where(t => t.DelegationId == del.Id)
                        .Select(t => t.TerminalId)
                        .ToListAsync(stoppingToken);

                    foreach (var terminalId in terminals)
                    {
                        // نحاول 3 مرات قبل الفشل
                        var success = false;
                        for (var i = 1; i <= 3 && !success; i++)
                        {
                            success = await api.UnassignOneAsync(del.EmployeeId, terminalId, stoppingToken);
                            if (!success) await Task.Delay(500, stoppingToken);
                        }
                    }

                    del.Status = "Expired";
                    del.ExpiredAt = now;

                    await activity.LogSystemAsync(
                        actorName: "DelegationWorker",
                        action: "Delegation.Expired",
                        entityType: "Delegation",
                        entityId: del.Id.ToString(),
                        summary: $"تم إنهاء انتداب للموظف {del.EmployeeId} ({terminals.Count} أجهزة).",
                        details: new { delegationId = del.Id, employeeId = del.EmployeeId, terminalCount = terminals.Count },
                        ct: stoppingToken
                    );
                }

                await db.SaveChangesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                // نسجل الخطأ بدل الصمت
                _logger.LogError("Something went wrong: " + ex.Message);
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }
}
