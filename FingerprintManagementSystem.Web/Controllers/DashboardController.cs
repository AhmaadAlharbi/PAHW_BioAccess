using FingerprintManagementSystem.ApiAdapter.Persistence;
using FingerprintManagementSystem.Web.Models;
using FingerprintManagementSystem.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace FingerprintManagementSystem.Web.Controllers;

public class DashboardController : Controller
{
    private readonly LocalAppDbContext _db;
    private readonly IActivityLogService _activity;

    public DashboardController(LocalAppDbContext db, IActivityLogService activity)
    {
        _db = db;
        _activity = activity;
    }

    [HttpGet("/dashboard")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var isAdmin = HttpContext.Session.GetString("IsAdmin") == "1";
        if (!isAdmin)
        {
            return View(new DashboardViewModel { IsAdmin = false });
        }

        var model = new DashboardViewModel
        {
            IsAdmin = isAdmin,
            RegionsCount = await _db.Regions.AsNoTracking().CountAsync(ct),
            MappingsCount = await _db.TerminalRegionMaps.AsNoTracking().CountAsync(ct),
            ActiveDelegationsCount = await _db.Delegations.AsNoTracking().CountAsync(x => x.Status == "Active", ct)
        };

        var latest = await _activity.GetLatestAsync(10, ct);

        static string? GetJsonString(JsonElement root, string name)
        {
            if (!root.TryGetProperty(name, out var prop)) return null;
            return prop.ValueKind == JsonValueKind.String ? prop.GetString() : prop.ToString();
        }

        static int? GetJsonInt(JsonElement root, string name)
        {
            if (!root.TryGetProperty(name, out var prop)) return null;
            return prop.ValueKind switch
            {
                JsonValueKind.Number when prop.TryGetInt32(out var i) => i,
                JsonValueKind.String when int.TryParse(prop.GetString(), out var s) => s,
                _ => null
            };
        }

        static string SummarizeList(IReadOnlyCollection<string> items, int show = 2)
        {
            if (items.Count == 0) return "";
            if (items.Count <= show) return string.Join("، ", items);
            return string.Join("، ", items.Take(show)) + $" +{items.Count - show}";
        }

        var activityMeta = latest.Select(a =>
        {
            string? terminalId = null;
            int? oldRegionId = null;
            int? newRegionId = null;
            int? regionId = null;
            int? employeeId = null;
            string? employeeName = null;
            string? newRegionName = null;
            int? count = null;

            if (!string.IsNullOrWhiteSpace(a.DetailsJson))
            {
                try
                {
                    using var doc = JsonDocument.Parse(a.DetailsJson);
                    var root = doc.RootElement;

                    terminalId = GetJsonString(root, "terminalId") ?? a.EntityId;
                    oldRegionId = GetJsonInt(root, "oldRegionId");
                    newRegionId = GetJsonInt(root, "newRegionId");
                    regionId = GetJsonInt(root, "regionId");

                    employeeId = GetJsonInt(root, "employeeId");
                    employeeName = GetJsonString(root, "employeeName");

                    newRegionName = GetJsonString(root, "newRegionName") ?? GetJsonString(root, "regionName");
                    count = GetJsonInt(root, "count");
                }
                catch
                {
                    terminalId = a.EntityId;
                }
            }
            else
            {
                terminalId = a.EntityId;
            }

            terminalId = string.IsNullOrWhiteSpace(terminalId) ? null : terminalId.Trim();
            employeeName = string.IsNullOrWhiteSpace(employeeName) ? null : employeeName.Trim();
            newRegionName = string.IsNullOrWhiteSpace(newRegionName) ? null : newRegionName.Trim();

            return new
            {
                Row = a,
                TerminalId = terminalId,
                OldRegionId = oldRegionId,
                NewRegionId = newRegionId,
                RegionId = regionId,
                EmployeeId = employeeId,
                EmployeeName = employeeName,
                NewRegionName = newRegionName,
                Count = count
            };
        }).ToList();

        var terminalIdsForActivity = activityMeta
            .Select(x => x.TerminalId)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var regionIdsForActivity = activityMeta
            .SelectMany(x => new[] { x.OldRegionId, x.NewRegionId, x.RegionId })
            .Where(x => x.HasValue && x.Value > 0)
            .Select(x => x!.Value)
            .Distinct()
            .ToList();

        var regionNamesById = regionIdsForActivity.Count == 0
            ? new Dictionary<int, string>()
            : await _db.Regions
                .AsNoTracking()
                .Where(r => regionIdsForActivity.Contains(r.Id))
                .ToDictionaryAsync(r => r.Id, r => r.Name, ct);

        var regionNameByTerminalId = terminalIdsForActivity.Count == 0
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : await (
                from m in _db.TerminalRegionMaps.AsNoTracking()
                join r in _db.Regions.AsNoTracking() on m.RegionId equals r.Id
                where terminalIdsForActivity.Contains(m.TerminalId)
                select new { m.TerminalId, RegionName = r.Name }
            ).ToDictionaryAsync(x => x.TerminalId, x => x.RegionName, StringComparer.OrdinalIgnoreCase, ct);

        string? TryGetRegionNameById(int? id)
        {
            if (!id.HasValue || id.Value <= 0) return null;
            return regionNamesById.TryGetValue(id.Value, out var name) ? name : null;
        }

        string? TryGetRegionNameByTerminalId(string? terminalId)
        {
            if (string.IsNullOrWhiteSpace(terminalId)) return null;
            return regionNameByTerminalId.TryGetValue(terminalId.Trim(), out var name) ? name : null;
        }

        model.LatestActivity = activityMeta.Select(x =>
        {
            var a = x.Row;

            var actorName = a.ActorName;
            if (!string.IsNullOrWhiteSpace(actorName))
            {
                var trimmed = actorName.Trim();
                if (trimmed.Length > 0 && trimmed.All(ch => ch == '?'))
                    actorName = null;
            }

            var actorText = a.ActorType == "System"
                ? (string.IsNullOrWhiteSpace(actorName) ? "النظام" : $"النظام ({actorName})")
                : (!string.IsNullOrWhiteSpace(actorName) && a.ActorEmployeeId.HasValue
                    ? $"{actorName} ({a.ActorEmployeeId.Value})"
                    : (actorName ?? (a.ActorEmployeeId.HasValue ? $"الموظف رقم {a.ActorEmployeeId.Value}" : "مستخدم")));

            var summary = a.Summary;

            var terminalId = x.TerminalId;
            var regionNameFromTerminal = TryGetRegionNameByTerminalId(terminalId);

            var employeeText = (x.EmployeeId.HasValue && !string.IsNullOrWhiteSpace(x.EmployeeName))
                ? $"{x.EmployeeName} ({x.EmployeeId.Value})"
                : (x.EmployeeId.HasValue ? $"الموظف رقم {x.EmployeeId.Value}" : null);

            if (a.Action == "TerminalRegion.Assigned")
            {
                var regionName = x.NewRegionName ?? TryGetRegionNameById(x.NewRegionId) ?? TryGetRegionNameById(x.RegionId);
                if (!string.IsNullOrWhiteSpace(terminalId) && !string.IsNullOrWhiteSpace(regionName))
                    summary = $"تم ربط جهاز البصمة \"{terminalId}\" بمنطقة \"{regionName}\".";
            }
            else if (a.Action == "TerminalRegion.Moved")
            {
                var oldName = TryGetRegionNameById(x.OldRegionId);
                var newName = x.NewRegionName ?? TryGetRegionNameById(x.NewRegionId) ?? TryGetRegionNameById(x.RegionId);
                if (!string.IsNullOrWhiteSpace(terminalId) && (!string.IsNullOrWhiteSpace(oldName) || !string.IsNullOrWhiteSpace(newName)))
                {
                    var fromText = !string.IsNullOrWhiteSpace(oldName) ? $"من منطقة \"{oldName}\" " : "";
                    var toText = !string.IsNullOrWhiteSpace(newName) ? $"إلى منطقة \"{newName}\"" : "إلى منطقة جديدة";
                    summary = $"تم نقل جهاز البصمة \"{terminalId}\" {fromText}{toText}.";
                }
            }
            else if (a.Action == "TerminalRegion.Cleared")
            {
                var oldName = TryGetRegionNameById(x.OldRegionId) ?? regionNameFromTerminal;
                if (!string.IsNullOrWhiteSpace(terminalId) && !string.IsNullOrWhiteSpace(oldName))
                    summary = $"تم فك ربط جهاز البصمة \"{terminalId}\" من منطقة \"{oldName}\".";
            }
            else if (a.Action == "TerminalRegion.BulkAssigned")
            {
                var regionName = x.NewRegionName ?? TryGetRegionNameById(x.RegionId);
                if (!string.IsNullOrWhiteSpace(regionName) && x.Count.HasValue && x.Count.Value > 0)
                    summary = $"تم توزيع/نقل {x.Count.Value} جهاز إلى منطقة \"{regionName}\".";
            }
            else if (a.Action == "EmployeeTerminal.Assigned" || a.Action == "EmployeeTerminal.Unassigned")
            {
                if (!string.IsNullOrWhiteSpace(employeeText) && !string.IsNullOrWhiteSpace(terminalId))
                {
                    var op = a.Action == "EmployeeTerminal.Assigned" ? "تم ربط" : "تم فك ربط";
                    summary = $"{op} {employeeText} بجهاز البصمة \"{terminalId}\".";
                    if (!string.IsNullOrWhiteSpace(regionNameFromTerminal))
                        summary = $"{summary} (منطقة \"{regionNameFromTerminal}\")";
                }
            }

            return new ActivityLogRowViewModel
            {
                TimeText = a.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
                ActorText = actorText,
                ActionText = DashboardViewModel.ToArabicAction(a.Action),
                Summary = summary
            };
        }).ToList();

        var delegationsFilter = (Request.Query["delegations"].ToString() ?? "").Trim().ToLowerInvariant();
        var filterValue = delegationsFilter switch
        {
            "scheduled" => "Scheduled",
            "all" => "All",
            _ => "Active"
        };
        model.DelegationsFilter = filterValue;

        var delegationsQuery =
            from d in _db.Delegations.AsNoTracking()
            join u in _db.AllowedUsers.AsNoTracking() on d.EmployeeId equals u.EmployeeId into users
            from u in users.DefaultIfEmpty()
            select new
            {
                d.Id,
                d.EmployeeId,
                EmployeeName = u.FullName,
                d.Status,
                d.StartDate,
                d.EndDate,
                TerminalsCount = d.Terminals.Count
            };

        delegationsQuery = filterValue switch
        {
            "Scheduled" => delegationsQuery.Where(x => x.Status == "Scheduled"),
            "All" => delegationsQuery,
            _ => delegationsQuery.Where(x => x.Status == "Active")
        };

        delegationsQuery = filterValue switch
        {
            "Scheduled" => delegationsQuery.OrderBy(x => x.StartDate),
            "All" => delegationsQuery
                .OrderBy(x => x.Status == "Active" ? 0 : x.Status == "Scheduled" ? 1 : 2)
                .ThenBy(x => x.Status == "Active" ? x.EndDate : x.Status == "Scheduled" ? x.StartDate : DateTime.MaxValue)
                .ThenByDescending(x => x.Status == "Expired" ? x.EndDate : DateTime.MinValue),
            _ => delegationsQuery.OrderBy(x => x.EndDate)
        };

        var delegations = await delegationsQuery.Take(10).ToListAsync(ct);

        var delegationIds = delegations.Select(d => d.Id).ToList();
        var delegationTerminals = new List<(int DelegationId, string TerminalId)>();
        if (delegationIds.Count > 0)
        {
            var rows = await _db.DelegationTerminals.AsNoTracking()
                .Where(t => delegationIds.Contains(t.DelegationId))
                .Select(t => new { t.DelegationId, t.TerminalId })
                .ToListAsync(ct);

            delegationTerminals = rows
                .Where(x => !string.IsNullOrWhiteSpace(x.TerminalId))
                .Select(x => (x.DelegationId, x.TerminalId.Trim()))
                .ToList();
        }

        var terminalsByDelegationId = delegationTerminals
            .GroupBy(x => x.DelegationId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.TerminalId).ToList());

        var delegationTerminalIds = delegationTerminals
            .Select(x => x.TerminalId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var regionNameByDelegationTerminalId = delegationTerminalIds.Count == 0
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : await (
                from m in _db.TerminalRegionMaps.AsNoTracking()
                join r in _db.Regions.AsNoTracking() on m.RegionId equals r.Id
                where delegationTerminalIds.Contains(m.TerminalId)
                select new { m.TerminalId, RegionName = r.Name }
            ).ToDictionaryAsync(x => x.TerminalId, x => x.RegionName, StringComparer.OrdinalIgnoreCase, ct);

        model.LatestDelegations = delegations.Select(d =>
        {
            string? devicesHintText = null;
            if (terminalsByDelegationId.TryGetValue(d.Id, out var terminals) && terminals.Count > 0)
            {
                var regionNames = terminals
                    .Select(t => regionNameByDelegationTerminalId.TryGetValue(t, out var rn) ? rn : null)
                    .Where(rn => !string.IsNullOrWhiteSpace(rn))
                    .Select(rn => rn!.Trim())
                    .Distinct()
                    .ToList();

                if (regionNames.Count > 0)
                {
                    devicesHintText = SummarizeList(regionNames, 2);
                }
                else
                {
                    var terminalIds = terminals.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                    devicesHintText = terminalIds.Count > 0 ? SummarizeList(terminalIds, 2) : null;
                }
            }

            return new DelegationRowViewModel
            {
                EmployeeText = string.IsNullOrWhiteSpace(d.EmployeeName)
                    ? $"الموظف رقم {d.EmployeeId}"
                    : $"{d.EmployeeName.Trim()} ({d.EmployeeId})",
                StatusText = DashboardViewModel.ToArabicDelegationStatus(d.Status),
                StatusBadgeClass = DashboardViewModel.ToDelegationStatusBadgeClass(d.Status),
                StartDateText = d.StartDate.ToString("yyyy-MM-dd"),
                EndDateText = d.EndDate.ToString("yyyy-MM-dd"),
                TerminalsCount = d.TerminalsCount,
                TerminalsCountText = d.TerminalsCount == 1 ? "جهاز واحد" : $"{d.TerminalsCount} أجهزة",
                DevicesHintText = devicesHintText
            };
        }).ToList();

        return View(model);
    }
}
