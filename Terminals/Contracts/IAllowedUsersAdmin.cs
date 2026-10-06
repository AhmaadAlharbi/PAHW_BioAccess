using Terminals.Web.DTOs;

namespace Terminals.Web.Contracts;

public interface IAllowedUsersAdmin
{
    Task<AllowedUserDto?> FetchFromSoapAsync(int employeeId, CancellationToken ct);
    Task<bool> AddAsync(AllowedUserDto dto, DateTime? validUntil, bool isAdmin, CancellationToken ct);
    
    Task<List<AllowedUserListItemDto>> ListAsync(CancellationToken ct);
    Task<AllowedUserListItemDto?> FindAsync(int employeeId, CancellationToken ct);
    Task<bool> UpdateAdministrativeAsync(int employeeId, string userType, DateTime? validUntil, bool isAdmin, bool isActive, int currentEmployeeId, CancellationToken ct);
    Task<bool> SetActiveAsync(int employeeId, bool isActive, CancellationToken ct);
    Task<bool> SetAdminAsync(int employeeId, bool isAdmin, CancellationToken ct);
    Task<bool> DeleteAsync(int employeeId, CancellationToken ct);

}
