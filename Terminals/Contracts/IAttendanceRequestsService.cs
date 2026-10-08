using Terminals.Web.DTOs;

namespace Terminals.Web.Contracts;

public interface IAttendanceRequestsService
{
    Task<int> CreateAsync(CreateAttendanceRequestDto dto, CancellationToken ct);
    Task<List<AttendanceRequestListItemDto>> ListAsync(AttendanceRequestListFilterDto filter, CancellationToken ct);
    Task<AttendanceRequestDetailsDto?> FindDetailsAsync(int id, CancellationToken ct);
}
