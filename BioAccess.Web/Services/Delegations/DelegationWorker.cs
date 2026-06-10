using BioAccess.Web.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using BioAccess.Web.Contracts;
using BioAccess.Web.Services.Activity;

namespace BioAccess.Web.Services.Delegations;

// Runs in the background and applies scheduled delegation changes in Alpeta.
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
        // Check delegations every minute so start and end dates are applied automatically.
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<LocalAppDbContext>();
                var api = scope.ServiceProvider.GetRequiredService<IEmployeeDevicesApi>();
                var activity = scope.ServiceProvider.GetRequiredService<IActivityLogService>();

                var now = DateTime.Now;

                // Start delegations whose time has arrived.
                var toStart = await db.Delegations
                    .Include(x => x.Terminals)
                    .Where(x => x.Status == "Scheduled" && x.StartDate <= now)
                    .ToListAsync(stoppingToken);

                foreach (var del in toStart)
                {
                    var terminals = del.Terminals
                        .Select(t => t.TerminalId)
                        .ToList();
                    var employeeText = FormatEmployeeText(null, del.EmployeeId);
                    var actorText = FormatActorText("DelegationWorker", null);

                    foreach (var terminalId in terminals)
                    {
                        // Retry a few times because Alpeta can fail temporarily.
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
                        summary: $"تم إنشاء انتداب للموظف {employeeText} لعدد ({terminals.Count}) أجهزة\nبواسطة: {actorText}",
                        details: new { delegationId = del.Id, employeeId = del.EmployeeId, terminalCount = terminals.Count },
                        ct: stoppingToken
                    );
                }

                // End delegations whose end date has passed.
                var toExpire = await db.Delegations
                    .Include(x => x.Terminals)
                    .Where(x => x.Status == "Active" && x.EndDate <= now)
                    .ToListAsync(stoppingToken);

                foreach (var del in toExpire)
                {
                    var terminals = del.Terminals
                        .Select(t => t.TerminalId)
                        .ToList();
                    var employeeText = FormatEmployeeText(null, del.EmployeeId);
                    var actorText = FormatActorText("DelegationWorker", null);

                    foreach (var terminalId in terminals)
                    {
                        // Retry a few times because Alpeta can fail temporarily.
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
                        summary: $"تم إنهاء انتداب الموظف {employeeText}\nبواسطة: {actorText}",
                        details: new { delegationId = del.Id, employeeId = del.EmployeeId, terminalCount = terminals.Count },
                        ct: stoppingToken
                    );
                }

                await db.SaveChangesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                // Keep the worker alive even if one cycle fails.
                _logger.LogError(ex, "Something went wrong while processing delegations.");
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }

    private static string FormatEmployeeText(string? employeeName, int employeeId)
        => string.IsNullOrWhiteSpace(employeeName)
            ? $"غير معروف ({employeeId})"
            : $"{employeeName.Trim()} ({employeeId})";

    private static string FormatActorText(string? actorName, string? actorId)
    {
        var name = string.IsNullOrWhiteSpace(actorName) ? "غير معروف" : actorName.Trim();
        var id = string.IsNullOrWhiteSpace(actorId) ? "غير معروف" : actorId.Trim();
        return $"{name} ({id})";
    }
}
