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
    public async Task<IActionResult> Index(string? delegations, CancellationToken ct)
    {
        var isAdmin = HttpContext.Session.GetString("IsAdmin") == "1";
        var empIdStr = HttpContext.Session.GetString("EmpId") ?? "";
        int.TryParse(empIdStr, out var currentUserEmpId);

        var dto = await _dashboardService.GetDashboardData(delegations, ct);

        return View(new DashboardViewModel
        {
            IsAdmin = isAdmin,
            CurrentUserEmpId = currentUserEmpId,
            RegionsCount = dto.RegionsCount,
            MappingsCount = dto.MappingsCount,
            ActiveDelegationsCount = dto.ActiveDelegationsCount,
            DelegationsFilter = dto.DelegationsFilter,
            LatestDelegations = dto.LatestDelegations.Select(x => new DelegationRowViewModel
            {
                EmployeeId = x.EmployeeId,
                EmployeeText = string.IsNullOrWhiteSpace(x.EmployeeName)
                    ? $"الموظف رقم {x.EmployeeId}"
                    : $"{x.EmployeeName} ({x.EmployeeId})",
                RegionText = x.RegionText,
                IsCurrentUser = currentUserEmpId > 0 && x.EmployeeId == currentUserEmpId,
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
