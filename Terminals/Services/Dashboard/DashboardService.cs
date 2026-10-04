using Terminals.Web.DTOs;
using Terminals.Web.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Terminals.Web.Services.Dashboard;

// Builds dashboard numbers and recent activity from local DB data.
public sealed class DashboardService
{
    private readonly LocalAppDbContext _db;

    public DashboardService(LocalAppDbContext db)
    {
        _db = db;
    }

    public async Task<DashboardDto> GetDashboardData(
        string? delegationsFilter = null,
        int activityPage = 1,
        int activityPageSize = 10,
        CancellationToken ct = default)
    {
        // Normalize the filter first so the rest of the query logic stays simple.
        var filter = NormalizeFilter(delegationsFilter);
        var now = DateTime.Now;
        activityPage = Math.Max(1, activityPage);
        activityPageSize = Math.Clamp(activityPageSize, 5, 50);
        var today = DateTime.Today;
        var tomorrow = today.AddDays(1);

        var activityTotalCount = await _db.ActivityLogs
            .AsNoTracking()
            .CountAsync(ct);
        var activityTotalPages = Math.Max(1, (int)Math.Ceiling(activityTotalCount / (double)activityPageSize));
        activityPage = Math.Min(activityPage, activityTotalPages);
        var activitySkip = (activityPage - 1) * activityPageSize;

        var todayActivityCount = await _db.ActivityLogs
            .AsNoTracking()
            .CountAsync(x => x.CreatedAt >= today && x.CreatedAt < tomorrow, ct);

        // Activity is paged so the dashboard can browse the full log without loading it all at once.
        var recentActivities = await _db.ActivityLogs
            .AsNoTracking()
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .Skip(activitySkip)
            .Take(activityPageSize)
            .Select(x => new ActivityLogDto
            {
                ActionType = x.Action,
                Summary = x.Summary,
                PerformedBy = x.ActorType == "System"
                    ? (string.IsNullOrWhiteSpace(x.ActorName) ? "النظام" : $"النظام ({x.ActorName})")
                    : (!string.IsNullOrWhiteSpace(x.ActorName) && x.ActorEmployeeId.HasValue
                        ? $"{x.ActorName} ({x.ActorEmployeeId.Value})"
                    : (x.ActorName ?? (x.ActorEmployeeId.HasValue ? $"الموظف رقم {x.ActorEmployeeId.Value}" : "عضو"))),
                CreatedAt = x.CreatedAt
            })
            .ToListAsync(ct);

        // Load raw delegations first, then compute the display status in code.
        var rawDelegations = await _db.Delegations
            .AsNoTracking()
            .Include(d => d.Terminals)
            .OrderByDescending(d => d.Id)
            .ToListAsync(ct);

        var employeeIds = rawDelegations
            .Select(d => d.EmployeeId)
            .Distinct()
            .ToList();

        // AllowedUsers is used here only to show friendly employee names.
        var employeeNameById = await _db.AllowedUsers
            .AsNoTracking()
            .Where(x => employeeIds.Contains(x.EmployeeId))
            .ToDictionaryAsync(x => x.EmployeeId, x => x.FullName, ct);

        var terminalIds = rawDelegations
            .SelectMany(d => d.Terminals.Select(t => (t.TerminalId ?? "").Trim()))
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Region names are derived from terminal mappings, not from the delegation table.
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

        // Build the dashboard list after joining employee names and region names.
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
            TodayActivityCount = todayActivityCount,
            ActivityPage = activityPage,
            ActivityPageSize = activityPageSize,
            ActivityTotalCount = activityTotalCount,
            RecentActivities = recentActivities,
            LatestDelegations = delegations,
            DelegationsFilter = filter
        };
    }

    private static string NormalizeFilter(string? filter)
        // Default to active delegations because that is the main dashboard view.
        => (filter ?? "").Trim().ToLowerInvariant() switch
        {
            "scheduled" => "Scheduled",
            "all" => "All",
            _ => "Active"
        };

    private static string GetComputedStatus(Persistence.Entities.Delegation delegation, DateTime now)
    {
        // Use computed status so the dashboard reflects time even before the worker runs.
        if (string.Equals(delegation.Status, "Cancelled", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(delegation.Status, "ManuallyEnded", StringComparison.OrdinalIgnoreCase))
        {
            return "Cancelled";
        }

        if (now < delegation.StartDate) return "Scheduled";
        if (now >= delegation.StartDate && now < delegation.EndDate) return "Active";
        return "Expired";
    }
}
