namespace Terminals.Web.Contracts;

public interface IAllowedUsersStore
{
    Task<bool> IsAllowedAsync(int employeeId, CancellationToken ct);
    Task<bool> IsAdminAsync(int employeeId, CancellationToken ct);
}
