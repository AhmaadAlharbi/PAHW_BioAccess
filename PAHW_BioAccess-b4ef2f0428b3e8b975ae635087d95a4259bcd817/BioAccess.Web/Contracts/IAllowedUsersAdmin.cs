using BioAccess.Web.DTOs;

namespace BioAccess.Web.Contracts;

public interface IAllowedUsersAdmin
{
    Task<AllowedUserDto?> FetchFromSoapAsync(int employeeId, CancellationToken ct);
    Task<bool> AddAsync(AllowedUserDto dto, DateTime? validUntil, bool isAdmin, CancellationToken ct);
    
    Task<List<AllowedUserListItemDto>> ListAsync(CancellationToken ct);
    Task<bool> SetActiveAsync(int employeeId, bool isActive, CancellationToken ct);
    Task<bool> DeleteAsync(int employeeId, CancellationToken ct);

}
