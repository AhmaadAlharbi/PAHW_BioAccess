using Terminals.Web.Models;
using Terminals.Web.Services.Dashboard;
using Microsoft.AspNetCore.Mvc;

namespace Terminals.Web.Controllers;

public sealed class DashboardController : Controller
{
    private readonly DashboardService _dashboardService;

    public DashboardController(DashboardService dashboardService)
    {
        _dashboardService = dashboardService;
    }

    [HttpGet("/dashboard")]
    public async Task<IActionResult> Index(string? delegations, CancellationToken ct)
    {
        var isAdmin = HttpContext.Session.GetString("IsAdmin") == "1";
        if (!isAdmin)
        {
            return View(new DashboardViewModel { IsAdmin = false });
        }

        var dto = await _dashboardService.GetDashboardData(delegations, ct);

        return View(new DashboardViewModel
        {
            IsAdmin = true,
            RegionsCount = dto.RegionsCount,
            MappingsCount = dto.MappingsCount,
            ActiveDelegationsCount = dto.ActiveDelegationsCount,
            DelegationsFilter = dto.DelegationsFilter,
            LatestDelegations = dto.LatestDelegations.Select(x => new DelegationRowViewModel
            {
                EmployeeText = string.IsNullOrWhiteSpace(x.EmployeeName)
                    ? $"Ø§Ù„Ù…ÙˆØ¸Ù Ø±Ù‚Ù… {x.EmployeeId}"
                    : $"{x.EmployeeName} ({x.EmployeeId})",
                RegionText = x.RegionText,
                StatusText = DashboardViewModel.ToArabicDelegationStatus(x.Status),
                StatusBadgeClass = DashboardViewModel.ToDelegationStatusBadgeClass(x.Status),
                StartDateText = x.StartDate.ToString("yyyy-MM-dd HH:mm"),
                EndDateText = x.EndDate.ToString("yyyy-MM-dd HH:mm"),
                TerminalsCount = x.TerminalsCount,
                TerminalsCountText = x.TerminalsCount.ToString()
            }).ToList(),
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
