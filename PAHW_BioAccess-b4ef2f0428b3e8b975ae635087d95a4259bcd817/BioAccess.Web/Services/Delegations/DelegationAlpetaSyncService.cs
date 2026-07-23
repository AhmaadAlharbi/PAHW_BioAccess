using Terminals.Web.External;
using Terminals.Web.Persistence.Entities;

namespace Terminals.Web.Services.Delegations;

// Applies delegation-related Alpeta assignment changes with guarded retries.
public class DelegationAlpetaSyncService
{
    private readonly AlpetaClient _alpeta;
    private readonly ILogger<DelegationAlpetaSyncService> _logger;

    public DelegationAlpetaSyncService(AlpetaClient alpeta, ILogger<DelegationAlpetaSyncService> logger)
    {
        _alpeta = alpeta;
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
            x => _alpeta.AssignUserToTerminalAsync(x, employeeId, ct),
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
            x => _alpeta.UnassignUserFromTerminalAsync(x, employeeId, ct),
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
}
