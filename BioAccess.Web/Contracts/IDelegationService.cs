namespace BioAccess.Web.Contracts;

public interface IDelegationService
{
    Task<string> SaveDelegationAsync(
        int employeeId,
        List<string> terminalIds,
        DateTime startDate,
        DateTime endDate,
        CancellationToken ct = default);

    Task<bool> EndActiveDelegationAsync(int delegationId, CancellationToken ct = default);
    Task<bool> CancelScheduledDelegationAsync(int delegationId, CancellationToken ct = default);
}
