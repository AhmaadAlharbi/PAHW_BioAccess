using Terminals.Web.Persistence;
using Terminals.Web.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Terminals.Web.Services.Activity;

namespace Terminals.Web.Services.Delegations;

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
                var activity = scope.ServiceProvider.GetRequiredService<IActivityLogService>();
                var alpetaSync = scope.ServiceProvider.GetRequiredService<DelegationAlpetaSyncService>();

                var now = DateTime.Now;

                var delegations = await db.Delegations
                    .Include(x => x.Terminals)
                    .Where(x => x.Status == "Scheduled" || x.Status == "Active")
                    .ToListAsync(stoppingToken);

                foreach (var del in delegations)
                {
                    if (del.Status == "Scheduled" && del.EndDate <= now)
                    {
                        del.Status = "Expired";
                        del.ExpiredAt = now;
                        continue;
                    }

                    if (del.Status == "Scheduled" && del.StartDate <= now && del.EndDate > now)
                    {
                        del.Status = "Active";
                    }

                    if (del.Status != "Active")
                        continue;

                    if (del.EndDate <= now)
                    {
                        await ExpireDelegationAsync(db, activity, alpetaSync, del, now, stoppingToken);
                        continue;
                    }

                    await EnsureActiveDelegationAsync(db, activity, alpetaSync, del, now, _logger, stoppingToken);
                }

                var staleDelegations = await db.Delegations
                    .Include(x => x.Terminals)
                    .Where(x =>
                        (x.Status == "Expired" || x.Status == "ManuallyEnded" || x.Status == "Cancelled") &&
                        x.Terminals.Any())
                    .ToListAsync(stoppingToken);

                foreach (var delegation in staleDelegations)
                    await RecoverDelegationCleanupAsync(alpetaSync, delegation, stoppingToken);

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

    private static Task<string?> GetEmployeeNameAsync(LocalAppDbContext db, int employeeId, CancellationToken ct)
        => db.AllowedUsers
            .AsNoTracking()
            .Where(x => x.EmployeeId == employeeId)
            .Select(x => x.FullName)
            .FirstOrDefaultAsync(ct);

    private static async Task EnsureActiveDelegationAsync(
        LocalAppDbContext db,
        IActivityLogService activity,
        DelegationAlpetaSyncService alpetaSync,
        Delegation del,
        DateTime now,
        ILogger<DelegationWorker> logger,
        CancellationToken ct)
    {
        if (del.Status != "Active" || del.StartDate > now || del.EndDate <= now)
            return;

        var wasScheduled = del.ActivatedAt is null;
        var snapshotReady = del.ActivatedAt is not null
            || await alpetaSync.TryCaptureActivationSnapshotAsync(del, now, ct);

        if (!snapshotReady)
            return;

        var terminals = (del.Terminals ?? new())
            .Where(t => !string.IsNullOrWhiteSpace(t.TerminalId))
            .ToList();

        foreach (var terminal in terminals)
        {
            var terminalId = terminal.TerminalId.Trim();

            if (terminal.IsManuallyRemoved)
            {
                logger.LogInformation(
                    "Delegation {DelegationId}: skipping assign for terminal {TerminalId} — manually removed by user.",
                    del.Id,
                    terminalId);
                continue;
            }

            await alpetaSync.EnsureAssignedAsync(del.Id, del.EmployeeId, terminalId, "active", ct);
        }

        if (!wasScheduled)
            return;

        var employeeName = await GetEmployeeNameAsync(db, del.EmployeeId, ct);
        var employeeText = FormatEmployeeText(employeeName, del.EmployeeId);
        var actorText = FormatActorText("DelegationWorker", null);

        await activity.LogSystemAsync(
            actorName: "DelegationWorker",
            action: "Delegation.Activated",
            entityType: "Delegation",
            entityId: del.Id.ToString(),
            summary: $"تم إنشاء انتداب للموظف {employeeText} لعدد ({terminals.Count}) أجهزة\nبواسطة: {actorText}",
            details: new { delegationId = del.Id, employeeId = del.EmployeeId, terminalCount = terminals.Count },
            ct: ct
        );
    }

    private static async Task ExpireDelegationAsync(
        LocalAppDbContext db,
        IActivityLogService activity,
        DelegationAlpetaSyncService alpetaSync,
        Delegation del,
        DateTime now,
        CancellationToken ct)
    {
        var terminals = (del.Terminals ?? new())
            .Where(t => !string.IsNullOrWhiteSpace(t.TerminalId))
            .ToList();

        del.Status = "Expired";
        del.ExpiredAt = now;

        foreach (var terminal in terminals)
        {
            var terminalId = terminal.TerminalId.Trim();
            if (terminal.WasAssignedBefore)
                continue;

            var coveredByAnotherDelegation = await db.DelegationTerminals
                .AnyAsync(
                    x => x.DelegationId != del.Id &&
                         x.Delegation != null &&
                         x.Delegation.EmployeeId == del.EmployeeId &&
                         x.TerminalId == terminalId &&
                         x.Delegation.Status == "Active" &&
                         x.Delegation.StartDate <= now &&
                         x.Delegation.EndDate > now,
                    ct);

            if (coveredByAnotherDelegation)
                continue;

            await alpetaSync.EnsureUnassignedAsync(del.Id, del.EmployeeId, terminalId, "expired", ct);
        }

        var employeeName = await GetEmployeeNameAsync(db, del.EmployeeId, ct);
        var employeeText = FormatEmployeeText(employeeName, del.EmployeeId);
        var actorText = FormatActorText("DelegationWorker", null);

        await activity.LogSystemAsync(
            actorName: "DelegationWorker",
            action: "Delegation.Expired",
            entityType: "Delegation",
            entityId: del.Id.ToString(),
            summary: $"تم إنهاء انتداب الموظف {employeeText}\nبواسطة: {actorText}",
            details: new { delegationId = del.Id, employeeId = del.EmployeeId, terminalCount = terminals.Count },
            ct: ct
        );
    }

    private static async Task RecoverDelegationCleanupAsync(
        DelegationAlpetaSyncService alpetaSync,
        Delegation delegation,
        CancellationToken ct)
    {
        await alpetaSync.CleanupDelegationAsync(
            delegation,
            delegation.Terminals,
            "recovery",
            ct);
    }
}
