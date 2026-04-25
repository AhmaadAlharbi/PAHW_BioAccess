using FingerprintManagementSystem.ApiAdapter.Persistence;
using FingerprintManagementSystem.Web.Models;
using FingerprintManagementSystem.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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
        model.LatestActivity = latest.Select(a =>
        {
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

            return new ActivityLogRowViewModel
            {
                TimeText = a.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
                ActorText = actorText,
                ActionText = DashboardViewModel.ToArabicAction(a.Action),
                Summary = a.Summary
            };
        }).ToList();

        return View(model);
    }
}
