using Terminals.Web.External;
using Terminals.Web.Persistence;
using Terminals.Web.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Terminals.Web.Services.Delegations;

// Applies delegation-related Alpeta assignment changes with guarded retries.
public class DelegationAlpetaSyncService
{
    private readonly AlpetaClient _alpeta;
    private readonly LocalAppDbContext _db;
    private readonly ILogger<DelegationAlpetaSyncService> _logger;

    public DelegationAlpetaSyncService(
        AlpetaClient alpeta,
        LocalAppDbContext db,
        ILogger<DelegationAlpetaSyncService> logger)
    {
        _alpeta = alpeta;
        _db = db;
        _logger = logger;
    }

    public async Task<bool> TryCaptureActivationSnapshotAsync(Delegation delegation, DateTime activatedAt, CancellationToken ct = default)
    {
        if (delegation.EmployeeId <= 0)
            return false;

        var terminals = (delegation.Terminals ?? new())
            .Where(t => !string.IsNullOrWhiteSpace(t.TerminalId))
            .ToList();

        if (terminals.Count == 0)
            return false;

        var result = await _alpeta.TryGetEmployeeDevicesAsync(delegation.EmployeeId, ct: ct);
        if (!result.Success)
        {
            _logger.LogWarning(
                "Delegation {DelegationId}: could not capture current Alpeta assignments for employee {EmployeeId}.",
                delegation.Id,
                delegation.EmployeeId);
            return false;
        }

        var assignedIds = result.Devices
            .Where(d => !string.IsNullOrWhiteSpace(d.DeviceId))
            .Select(d => d.DeviceId!.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var terminal in terminals)
            terminal.WasAssignedBefore = assignedIds.Contains(terminal.TerminalId.Trim());

        delegation.ActivatedAt ??= activatedAt;
        return true;
    }

    public async Task<IReadOnlyCollection<string>> CleanupDelegationAsync(
        Delegation delegation,
        IEnumerable<DelegationTerminal>? terminals = null,
        string reason = "cleanup",
        CancellationToken ct = default)
    {
        var terminalRows = (terminals ?? delegation.Terminals ?? new())
            .Where(t => !string.IsNullOrWhiteSpace(t.TerminalId))
            .GroupBy(t => t.TerminalId.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        _logger.LogInformation(
            "Delegation {DelegationId}: cleanup triggered, reason={Reason}, employeeId={EmployeeId}, terminalCount={TerminalCount}",
            delegation.Id,
            reason,
            delegation.EmployeeId,
            terminalRows.Count);

        if (delegation.EmployeeId <= 0 || terminalRows.Count == 0)
            return Array.Empty<string>();

        var cleanedTerminalIds = new List<string>(terminalRows.Count);

        foreach (var terminal in terminalRows)
        {
            var terminalId = terminal.TerminalId.Trim();
            if (terminal.WasAssignedBefore)
            {
                _logger.LogInformation(
                    "Delegation {DelegationId}: cleanup skipped for terminal={TerminalId}, reason={Reason}, WasAssignedBefore=true",
                    delegation.Id,
                    terminalId,
                    reason);
                cleanedTerminalIds.Add(terminalId);
                continue;
            }

            var coveredByAnotherDelegation = await IsCoveredByAnotherActiveDelegationAsync(
                delegation.Id,
                delegation.EmployeeId,
                terminalId,
                ct);

            if (coveredByAnotherDelegation)
            {
                _logger.LogInformation(
                    "Delegation {DelegationId}: cleanup skipped for terminal={TerminalId}, reason={Reason}, coveredByAnotherActiveDelegation=true",
                    delegation.Id,
                    terminalId,
                    reason);
                cleanedTerminalIds.Add(terminalId);
                continue;
            }

            var unassignSuccess = await EnsureUnassignedAsync(
                delegation.Id,
                delegation.EmployeeId,
                terminalId,
                reason,
                ct);

            if (!unassignSuccess)
            {
                _logger.LogWarning(
                    "Delegation {DelegationId}: cleanup unassign failed for terminal={TerminalId}, reason={Reason}",
                    delegation.Id,
                    terminalId,
                    reason);
                continue;
            }

            var assignedAfterCleanup = await IsAssignedInAlpetaAsync(delegation.EmployeeId, terminalId, ct);
            if (assignedAfterCleanup is false)
            {
                cleanedTerminalIds.Add(terminalId);
                _logger.LogInformation(
                    "Delegation {DelegationId}: cleanup confirmed terminal={TerminalId} removed from Alpeta, reason={Reason}",
                    delegation.Id,
                    terminalId,
                    reason);
                continue;
            }

            _logger.LogWarning(
                "Delegation {DelegationId}: cleanup could not confirm terminal={TerminalId} removal from Alpeta, reason={Reason}, stillAssigned={StillAssigned}",
                delegation.Id,
                terminalId,
                reason,
                assignedAfterCleanup);
        }

        var cleanedEntities = terminalRows
            .Where(t => cleanedTerminalIds.Contains(t.TerminalId.Trim(), StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (cleanedEntities.Count > 0)
        {
            _db.DelegationTerminals.RemoveRange(cleanedEntities);
            _logger.LogInformation(
                "Delegation {DelegationId}: cleanup complete, queued {Count} terminal row(s) for removal from DB, reason={Reason}",
                delegation.Id,
                cleanedEntities.Count,
                reason);
        }

        var finalRead = await _alpeta.TryGetEmployeeDevicesAsync(delegation.EmployeeId, ct: ct);
        if (!finalRead.Success)
        {
            _logger.LogWarning(
                "Delegation {DelegationId}: final Alpeta read failed after cleanup for employee {EmployeeId}; skipping state confirmation refresh.",
                delegation.Id,
                delegation.EmployeeId);
        }
        return cleanedTerminalIds;
    }

    public Task<bool> EnsureAssignedAsync(
        int delegationId,
        int employeeId,
        string? terminalId,
        string reason = "active",
        CancellationToken ct = default)
        => ExecuteWithRetryAsync(
            delegationId,
            employeeId,
            terminalId,
            "assign",
            reason,
            async x => (await _alpeta.AssignUserToTerminalAsync(x, employeeId, ct)).Success,
            ct);

    public Task<bool> EnsureUnassignedAsync(
        int delegationId,
        int employeeId,
        string? terminalId,
        string reason = "expired",
        CancellationToken ct = default)
        => ExecuteWithRetryAsync(
            delegationId,
            employeeId,
            terminalId,
            "unassign",
            reason,
            async x => (await _alpeta.UnassignUserFromTerminalAsync(x, employeeId, ct)).Success,
            ct);

    private async Task<bool> ExecuteWithRetryAsync(
        int delegationId,
        int employeeId,
        string? terminalId,
        string action,
        string reason,
        Func<string, Task<bool>> execute,
        CancellationToken ct)
    {
        var trimmedTerminalId = terminalId?.Trim();
        if (employeeId <= 0 || string.IsNullOrWhiteSpace(trimmedTerminalId))
        {
            _logger.LogWarning(
                "Delegation {DelegationId}: skipping Alpeta {Action} because employeeId or terminalId is invalid. EmployeeId={EmployeeId}, TerminalId={TerminalId}, Reason={Reason}",
                delegationId,
                action,
                employeeId,
                terminalId,
                reason);
            return false;
        }

        var success = false;
        for (var attempt = 1; attempt <= 3 && !success; attempt++)
        {
            success = await execute(trimmedTerminalId);
            if (!success && attempt < 3)
                await Task.Delay(500, ct);
        }

        _logger.LogInformation(
            "Delegation {DelegationId}: Alpeta {Action} for employee {EmployeeId}, terminal {TerminalId}, reason={Reason}, result={Result}",
            delegationId,
            action,
            employeeId,
            trimmedTerminalId,
            reason,
            success ? "success" : "failed");

        return success;
    }

    private async Task<bool> IsCoveredByAnotherActiveDelegationAsync(
        int delegationId,
        int employeeId,
        string terminalId,
        CancellationToken ct)
        => await _db.DelegationTerminals.AnyAsync(
            x => x.DelegationId != delegationId &&
                 x.Delegation != null &&
                 x.Delegation.EmployeeId == employeeId &&
                 x.TerminalId == terminalId &&
                 x.Delegation.Status == "Active" &&
                 x.Delegation.StartDate <= DateTime.Now &&
                 x.Delegation.EndDate > DateTime.Now,
            ct);

    private async Task<bool?> IsAssignedInAlpetaAsync(int employeeId, string terminalId, CancellationToken ct)
    {
        var result = await _alpeta.TryGetEmployeeDevicesAsync(employeeId, ct: ct);
        if (!result.Success)
            return null;

        return result.Devices.Any(x => string.Equals(x.DeviceId?.Trim(), terminalId, StringComparison.OrdinalIgnoreCase));
    }
}
