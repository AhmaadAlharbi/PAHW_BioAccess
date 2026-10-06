using Terminals.Web.DTOs;

namespace Terminals.Web.Contracts;

public interface IAttendanceRequestsService
{
    Task<int> CreateAsync(CreateAttendanceRequestDto dto, CancellationToken ct);
}
