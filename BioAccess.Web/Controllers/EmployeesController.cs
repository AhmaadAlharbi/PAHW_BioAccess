using BioAccess.Web.Contracts;
using BioAccess.Web.DTOs;
using BioAccess.Web.Services.Activity;
using BioAccess.Web.Services.Employees;
using Microsoft.AspNetCore.Mvc;

namespace BioAccess.Web.Controllers;

[Route("employees")]
// Handles employee search, device assignment, and delegation actions.
public sealed class EmployeesController : Controller
{
    private readonly EmployeeDevicesApi _employees;
    private readonly IDelegationService _delegations;
    private readonly IActivityLogService _activityLog;

    public EmployeesController(EmployeeDevicesApi employees, IDelegationService delegations, IActivityLogService activityLog)
    {
        _employees = employees;
        _delegations = delegations;
        _activityLog = activityLog;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(int? employeeId, CancellationToken ct)
    {
        // Show the search page first when no employee is selected.
        if (!employeeId.HasValue || employeeId.Value <= 0)
            return View("Search", null);

        var screen = await _employees.GetEmployeeDevicesScreenAsync(employeeId.Value, ct);
        return View("Search", screen);
    }

    [HttpGet("search")]
    public async Task<IActionResult> Search(int employeeId, CancellationToken ct)
    {
        // Load the full employee screen for a direct search request.
        if (employeeId <= 0) return View("Search");

        var screen = await _employees.GetEmployeeDevicesScreenAsync(employeeId, ct);
        TempData["LastEmployeeId"] = employeeId.ToString();
        return View("Search", screen);
    }

    [HttpPost("search")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SearchPost([FromForm] int employeeId, CancellationToken ct)
    {
        // POST version used by the search form on the page.
        var screen = await _employees.GetEmployeeDevicesScreenAsync(employeeId, ct);
        TempData["LastEmployeeId"] = employeeId.ToString();

        if (screen?.Employee is null)
        {
            // Keep the user on the same page and show a clear message.
            TempData["ToastType"] = "danger";
            TempData["ToastMsg"] = $"لا يوجد موظف بالرقم الوظيفي: {employeeId}";
            return View("Search");
        }

        return View("Search", screen);
    }

    [HttpPost("assign")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Assign(int employeeId, string terminalId, CancellationToken ct)
    {
        // Assign one terminal to the employee in Alpeta.
        var result = await _employees.AssignOneAsync(employeeId, terminalId, ct);
        TempData["ToastType"] = result.Success ? "success" : "danger";
        TempData["ToastMsg"] = (result.Success ? "✅ " : "❌ ") + result.Message;
        TempData["LastEmployeeId"] = employeeId.ToString();

        var screen = await _employees.GetEmployeeDevicesScreenAsync(employeeId, ct);
        if (result.Success)
        {
            // Log only after reloading the screen so the log can include region info.
            var employeeText = FormatEmployeeText(screen?.Employee?.FullNameAr, employeeId);
            var actorText = FormatActorText(HttpContext.Session.GetString("EmpName"), HttpContext.Session.GetString("EmpId"));
            var regionLine = FormatRegionLine(screen?.Devices, new[] { terminalId });
            await _activityLog.LogAsync(
                "EmployeeTerminal.Assigned",
                "EmployeeTerminal",
                terminalId,
                $"تم ربط أجهزة عددها (1) للموظف {employeeText}{regionLine}\nبواسطة: {actorText}");
        }
        return View("Search", screen);
    }

    [HttpPost("unassign")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Unassign(int employeeId, string terminalId, CancellationToken ct)
    {
        // Remove one terminal from the employee in Alpeta.
        var result = await _employees.UnassignOneAsync(employeeId, terminalId, ct);
        TempData["ToastType"] = result.Success ? "success" : "danger";
        TempData["ToastMsg"] = (result.Success ? "✅ " : "❌ ") + result.Message;
        TempData["LastEmployeeId"] = employeeId.ToString();

        var screen = await _employees.GetEmployeeDevicesScreenAsync(employeeId, ct);
        if (result.Success)
        {
            var employeeText = FormatEmployeeText(screen?.Employee?.FullNameAr, employeeId);
            var actorText = FormatActorText(HttpContext.Session.GetString("EmpName"), HttpContext.Session.GetString("EmpId"));
            var regionLine = FormatRegionLine(screen?.Devices, new[] { terminalId });
            await _activityLog.LogAsync(
                "EmployeeTerminal.Unassigned",
                "EmployeeTerminal",
                terminalId,
                $"تم فك ربط أجهزة عددها (1) عن الموظف {employeeText}{regionLine}\nبواسطة: {actorText}");
        }
        return View("Search", screen);
    }

    [HttpPost("AssignBulk")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AssignBulk(int employeeId, List<string> terminalIds, CancellationToken ct)
    {
        // Run bulk assignment one terminal at a time.
        terminalIds ??= new List<string>();
        var successCount = 0;
        var totalCount = terminalIds.Count;
        var successfulTerminalIds = new List<string>();
        foreach (var terminalId in terminalIds)
        {
            if ((await _employees.AssignOneAsync(employeeId, terminalId, ct)).Success)
            {
                successCount++;
                successfulTerminalIds.Add(terminalId);
            }
        }

        if (successCount == 0)
        {
            TempData["ToastType"] = "danger";
            TempData["ToastMsg"] = "❌ لم يتم تنفيذ أي عملية";
        }
        else if (successCount < totalCount)
        {
            TempData["ToastType"] = "warning";
            TempData["ToastMsg"] = $"⚠️ تم تنفيذ جزئي ({successCount}/{totalCount})";
        }
        else
        {
            TempData["ToastType"] = "success";
            TempData["ToastMsg"] = $"✅ تم ربط {successCount} جهاز";
        }

        var screen = await _employees.GetEmployeeDevicesScreenAsync(employeeId, ct);
        if (successCount > 0)
        {
            var employeeText = FormatEmployeeText(screen?.Employee?.FullNameAr, employeeId);
            var actorText = FormatActorText(HttpContext.Session.GetString("EmpName"), HttpContext.Session.GetString("EmpId"));
            var regionLine = FormatRegionLine(screen?.Devices, successfulTerminalIds);
            await _activityLog.LogAsync(
                "EmployeeTerminal.BulkAssigned",
                "EmployeeTerminal",
                null,
                $"تم ربط أجهزة عددها ({successCount}) للموظف {employeeText}{regionLine}\nبواسطة: {actorText}");
        }
        return View("Search", screen);
    }

    [HttpPost("UnassignBulk")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UnassignBulk(int employeeId, List<string> terminalIds, CancellationToken ct)
    {
        // Run bulk unassignment one terminal at a time.
        terminalIds ??= new List<string>();
        var successCount = 0;
        var totalCount = terminalIds.Count;
        var successfulTerminalIds = new List<string>();
        foreach (var terminalId in terminalIds)
        {
            if ((await _employees.UnassignOneAsync(employeeId, terminalId, ct)).Success)
            {
                successCount++;
                successfulTerminalIds.Add(terminalId);
            }
        }

        if (successCount == 0)
        {
            TempData["ToastType"] = "danger";
            TempData["ToastMsg"] = "❌ لم يتم تنفيذ أي عملية";
        }
        else if (successCount < totalCount)
        {
            TempData["ToastType"] = "warning";
            TempData["ToastMsg"] = $"⚠️ تم تنفيذ جزئي ({successCount}/{totalCount})";
        }
        else
        {
            TempData["ToastType"] = "success";
            TempData["ToastMsg"] = $"✅ تم فك الارتباط عن {successCount} جهاز";
        }

        var screen = await _employees.GetEmployeeDevicesScreenAsync(employeeId, ct);
        if (successCount > 0)
        {
            var employeeText = FormatEmployeeText(screen?.Employee?.FullNameAr, employeeId);
            var actorText = FormatActorText(HttpContext.Session.GetString("EmpName"), HttpContext.Session.GetString("EmpId"));
            var regionLine = FormatRegionLine(screen?.Devices, successfulTerminalIds);
            await _activityLog.LogAsync(
                "EmployeeTerminal.BulkUnassigned",
                "EmployeeTerminal",
                null,
                $"تم فك ربط أجهزة عددها ({successCount}) عن الموظف {employeeText}{regionLine}\nبواسطة: {actorText}");
        }
        return View("Search", screen);
    }

    [HttpPost("DelegateRegion")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DelegateRegion(
        int employeeId,
        List<string> terminalIds,
        DateTime startDate,
        DateTime endDate,
        CancellationToken ct)
    {
        // Save a temporary device delegation for later worker processing.
        var result = await _delegations.SaveDelegationAsync(employeeId, terminalIds, startDate, endDate, ct);
        if (result == "Invalid")
        {
            TempData["ToastType"] = "danger";
            TempData["ToastMsg"] = "❌ فشل في حفظ الانتداب، تحقق من التواريخ";
        }
        else
        {
            TempData["ToastType"] = "success";
            TempData["ToastMsg"] = "✅ تم جدولة الانتداب بنجاح";
        }

        var screen = await _employees.GetEmployeeDevicesScreenAsync(employeeId, ct);
        return View("Search", screen);
    }

    [HttpPost("end-delegation")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EndDelegation(int employeeId, List<string> terminalIds, CancellationToken ct)
    {
        // End active delegations for the selected terminals.
        var ok = await _delegations.EndActiveDelegationAsync(employeeId, terminalIds, ct);
        TempData["ToastType"] = ok ? "success" : "danger";
        TempData["ToastMsg"] = ok ? "✅ تم إنهاء الندب بنجاح" : "❌ تعذر إنهاء الندب";

        var screen = await _employees.GetEmployeeDevicesScreenAsync(employeeId, ct);
        return View("Search", screen);
    }

    [HttpPost("cancel-delegation")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelDelegation(int employeeId, int delegationId, CancellationToken ct)
    {
        // Cancel a delegation that has not started yet.
        var ok = await _delegations.CancelScheduledDelegationAsync(delegationId, ct);
        TempData["ToastType"] = ok ? "success" : "danger";
        TempData["ToastMsg"] = ok ? "✅ تم إلغاء الندب بنجاح" : "❌ تعذر إلغاء الندب";

        var screen = await _employees.GetEmployeeDevicesScreenAsync(employeeId, ct);
        return View("Search", screen);
    }

    private static string FormatEmployeeText(string? employeeName, int employeeId)
        => string.IsNullOrWhiteSpace(employeeName)
            ? $"غير معروف ({employeeId})"
            : $"{employeeName.Trim()} ({employeeId})";

    private static string FormatActorText(string? actorName, string? actorId)
    {
        var name = string.IsNullOrWhiteSpace(actorName) ? "غير معروف" : actorName.Trim();
        var id = string.IsNullOrWhiteSpace(actorId) ? "غير معروف" : actorId.Trim();
        return $"{name} ({id})";
    }

    private static string FormatRegionLine(IEnumerable<DeviceRowDto>? devices, IEnumerable<string> terminalIds)
    {
        // Build one region line for logs so bulk actions stay readable.
        var selectedIds = terminalIds
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (selectedIds.Count == 0 || devices is null)
            return "";

        var regionNames = devices
            .Where(d => selectedIds.Contains((d.DeviceId ?? "").Trim()))
            .Select(d => d.RegionName)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return regionNames.Count == 0
            ? ""
            : $"\nفي المناطق: {string.Join("، ", regionNames)}";
    }
}
