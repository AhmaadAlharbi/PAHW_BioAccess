using Terminals.Web.Contracts;
using Terminals.Web.DTOs;
using Terminals.Web.Persistence;
using Terminals.Web.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

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

    public async Task<List<AttendanceRequestListItemDto>> ListAsync(AttendanceRequestListFilterDto filter, CancellationToken ct)
    {
        var query = _db.AttendanceRequests.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(filter.Month) &&
            DateTime.TryParse($"{filter.Month}-01", out var monthStart))
        {
            var monthEnd = monthStart.AddMonths(1);
            query = query.Where(x => x.AttendanceDate >= monthStart && x.AttendanceDate < monthEnd);
        }

        if (!string.IsNullOrWhiteSpace(filter.EmployeeId))
        {
            if (int.TryParse(filter.EmployeeId.Trim(), out var employeeId))
            {
                query = query.Where(x => x.EmployeeId == employeeId);
            }
            else
            {
                query = query.Where(x => false);
            }
        }

        if (!string.IsNullOrWhiteSpace(filter.AttendanceDate) &&
            DateTime.TryParse(filter.AttendanceDate, out var attendanceDate))
        {
            query = query.Where(x => x.AttendanceDate.Date == attendanceDate.Date);
        }

        if (filter.Status is "PendingIT" or "Answered")
        {
            query = query.Where(x => x.Status == filter.Status);
        }

        return await query
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new AttendanceRequestListItemDto(
                x.Id,
                x.EmployeeId,
                x.EmployeeName,
                x.AttendanceDate,
                x.CreatedByEmployeeId,
                x.CreatedByName,
                x.Status,
                x.CreatedAt
            ))
            .ToListAsync(ct);
    }

    public async Task<AttendanceRequestDetailsDto?> FindDetailsAsync(int id, CancellationToken ct)
    {
        return await _db.AttendanceRequests
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new AttendanceRequestDetailsDto
            {
                Id = x.Id,
                EmployeeId = x.EmployeeId,
                EmployeeName = x.EmployeeName,
                AttendanceDate = x.AttendanceDate,
                CreatedByEmployeeId = x.CreatedByEmployeeId,
                CreatedByName = x.CreatedByName,
                HrNote = x.HrNote,
                Status = x.Status,
                CreatedAt = x.CreatedAt,
                Items = x.Items
                    .OrderBy(item => item.Id)
                    .Select(item => new AttendanceRequestItemDetailsDto(
                        item.Type,
                        item.Reply
                    ))
                    .ToList()
            })
            .FirstOrDefaultAsync(ct);
    }
}
