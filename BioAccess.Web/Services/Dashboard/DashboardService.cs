using BioAccess.Web.DTOs;
using BioAccess.Web.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BioAccess.Web.Services.Dashboard;

public sealed class DashboardService
{
    private readonly LocalAppDbContext _db;

    public DashboardService(LocalAppDbContext db)
    {
        _db = db;
    }

    public async Task<DashboardDto> GetDashboardData(CancellationToken ct = default)
    {
        var recentActivities = await _db.ActivityLogs
            .AsNoTracking()
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .Take(10)
            .Select(x => new ActivityLogDto
            {
                ActionType = x.Action,
                Summary = x.Summary,
                PerformedBy = x.ActorType == "System"
                    ? (string.IsNullOrWhiteSpace(x.ActorName) ? "النظام" : $"النظام ({x.ActorName})")
                    : (!string.IsNullOrWhiteSpace(x.ActorName) && x.ActorEmployeeId.HasValue
                        ? $"{x.ActorName} ({x.ActorEmployeeId.Value})"
                        : (x.ActorName ?? (x.ActorEmployeeId.HasValue ? $"الموظف رقم {x.ActorEmployeeId.Value}" : "مستخدم"))),
                CreatedAt = x.CreatedAt
            })
            .ToListAsync(ct);

        return new DashboardDto
        {
            RegionsCount = await _db.Regions.AsNoTracking().CountAsync(ct),
            MappingsCount = await _db.TerminalRegionMaps.AsNoTracking().CountAsync(ct),
            ActiveDelegationsCount = await _db.Delegations.AsNoTracking().CountAsync(x => x.Status == "Active", ct),
            RecentActivities = recentActivities
        };
    }
}
