using BioAccess.Web.Models;
using BioAccess.Web.Services.Dashboard;
using Microsoft.AspNetCore.Mvc;

namespace BioAccess.Web.Controllers;

public sealed class DashboardController : Controller
{
    private readonly DashboardService _dashboardService;

    public DashboardController(DashboardService dashboardService)
    {
        _dashboardService = dashboardService;
    }

    [HttpGet("/dashboard")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var isAdmin = HttpContext.Session.GetString("IsAdmin") == "1";
        if (!isAdmin)
        {
            return View(new DashboardViewModel { IsAdmin = false });
        }

        var dto = await _dashboardService.GetDashboardData(ct);

        return View(new DashboardViewModel
        {
            IsAdmin = true,
            RegionsCount = dto.RegionsCount,
            MappingsCount = dto.MappingsCount,
            ActiveDelegationsCount = dto.ActiveDelegationsCount,
            LatestActivity = dto.RecentActivities.Select(x => new ActivityLogRowViewModel
            {
                TimeText = x.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
                ActorText = x.PerformedBy,
                ActionText = DashboardViewModel.ToArabicAction(x.ActionType),
                Summary = x.Summary
            }).ToList()
        });
    }
}
