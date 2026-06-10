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

    public async Task<DashboardDto> GetDashboardData(string? delegationsFilter = null, CancellationToken ct = default)
    {
        var filter = NormalizeFilter(delegationsFilter);
        var now = DateTime.Now;

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

        var rawDelegations = await _db.Delegations
            .AsNoTracking()
            .Include(d => d.Terminals)
            .OrderByDescending(d => d.Id)
            .ToListAsync(ct);

        var employeeIds = rawDelegations
            .Select(d => d.EmployeeId)
            .Distinct()
            .ToList();

        var employeeNameById = await _db.AllowedUsers
            .AsNoTracking()
            .Where(x => employeeIds.Contains(x.EmployeeId))
            .ToDictionaryAsync(x => x.EmployeeId, x => x.FullName, ct);

        var terminalIds = rawDelegations
            .SelectMany(d => d.Terminals.Select(t => (t.TerminalId ?? "").Trim()))
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var regionNamesByTerminalId = await _db.TerminalRegionMaps
            .AsNoTracking()
            .Where(x => terminalIds.Contains(x.TerminalId))
            .Select(x => new
            {
                x.TerminalId,
                RegionName = x.Region != null ? x.Region.Name : null
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.RegionName))
            .ToDictionaryAsync(x => x.TerminalId, x => x.RegionName!, StringComparer.OrdinalIgnoreCase, ct);

        var delegations = rawDelegations
            .Select(d =>
            {
                var status = GetComputedStatus(d, now);
                var delegationTerminalIds = d.Terminals
                    .Select(t => (t.TerminalId ?? "").Trim())
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var regionNames = delegationTerminalIds
                    .Where(regionNamesByTerminalId.ContainsKey)
                    .Select(t => regionNamesByTerminalId[t])
                    .Distinct()
                    .ToList();

                employeeNameById.TryGetValue(d.EmployeeId, out var employeeName);

                return new DelegationDto
                {
                    EmployeeId = d.EmployeeId,
                    EmployeeName = employeeName ?? "",
                    StartDate = d.StartDate,
                    EndDate = d.EndDate,
                    Status = status,
                    TerminalsCount = delegationTerminalIds.Count,
                    RegionText = regionNames.Count > 0
                        ? $"في المناطق: {string.Join("، ", regionNames)}"
                        : ""
                };
            })
            .Where(d => filter == "All" || d.Status == filter)
            .Take(10)
            .ToList();

        return new DashboardDto
        {
            RegionsCount = await _db.Regions.AsNoTracking().CountAsync(ct),
            MappingsCount = await _db.TerminalRegionMaps.AsNoTracking().CountAsync(ct),
            ActiveDelegationsCount = rawDelegations.Count(d => GetComputedStatus(d, now) == "Active"),
            RecentActivities = recentActivities,
            LatestDelegations = delegations,
            DelegationsFilter = filter
        };
    }

    private static string NormalizeFilter(string? filter)
        => (filter ?? "").Trim().ToLowerInvariant() switch
        {
            "scheduled" => "Scheduled",
            "all" => "All",
            _ => "Active"
        };

    private static string GetComputedStatus(Persistence.Entities.Delegation delegation, DateTime now)
    {
        if (string.Equals(delegation.Status, "Cancelled", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(delegation.Status, "ManuallyEnded", StringComparison.OrdinalIgnoreCase))
        {
            return "Cancelled";
        }

        if (now < delegation.StartDate) return "Scheduled";
        if (now >= delegation.StartDate && now <= delegation.EndDate) return "Active";
        return "Expired";
    }
}
