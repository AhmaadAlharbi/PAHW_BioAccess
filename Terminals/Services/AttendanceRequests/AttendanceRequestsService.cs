using Terminals.Web.Contracts;
using Terminals.Web.DTOs;
using Terminals.Web.Persistence;
using Terminals.Web.Persistence.Entities;

namespace Terminals.Web.Services.AttendanceRequests;

public class AttendanceRequestsService : IAttendanceRequestsService
{
    private readonly LocalAppDbContext _db;

    public AttendanceRequestsService(LocalAppDbContext db)
    {
        _db = db;
    }

    public async Task<int> CreateAsync(CreateAttendanceRequestDto dto, CancellationToken ct)
    {
        var request = new AttendanceRequest
        {
            EmployeeId = dto.EmployeeId,
            EmployeeName = dto.EmployeeName,
            AttendanceDate = dto.AttendanceDate,
            HrNote = string.IsNullOrWhiteSpace(dto.HrNote) ? null : dto.HrNote.Trim(),
            Status = "PendingIT",
            CreatedAt = DateTime.Now,
            CreatedByEmployeeId = dto.CreatedByEmployeeId,
            CreatedByName = dto.CreatedByName,
            Items = dto.Types.Select(type => new AttendanceRequestItem
            {
                Type = type
            }).ToList()
        };

        _db.AttendanceRequests.Add(request);
        await _db.SaveChangesAsync(ct);

        return request.Id;
    }
}
