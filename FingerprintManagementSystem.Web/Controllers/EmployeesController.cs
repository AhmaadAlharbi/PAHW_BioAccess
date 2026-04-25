using FingerprintManagementSystem.Contracts;
using FingerprintManagementSystem.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace FingerprintManagementSystem.Web.Controllers;

[Route("employees")]
public class EmployeesController : Controller
{
    private readonly IEmployeeDevicesApi _api;
    private readonly IDelegationService _delegations;
    private readonly IActivityLogService _activity;

    public EmployeesController(IEmployeeDevicesApi api, IDelegationService delegations, IActivityLogService activity)
    {
        _api = api;
        _delegations = delegations;
        _activity = activity;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(int? employeeId, CancellationToken ct)
    {
        if (!employeeId.HasValue || employeeId.Value <= 0)
            return View("Search", null); // صفحة البحث فقط بدون نتائج

        var screen = await _api.GetEmployeeDevicesScreenAsync(employeeId.Value, ct);
        return View("Search", screen);
    }

    [HttpGet("search")]
    public async Task<IActionResult> Search(int employeeId, CancellationToken ct)
    {
        if (employeeId <= 0) return View("Search");

        var screen = await _api.GetEmployeeDevicesScreenAsync(employeeId, ct);
        TempData["LastEmployeeId"] = employeeId.ToString();
        return View("Search", screen);
    }

    [HttpPost("search")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SearchPost([FromForm] int employeeId, CancellationToken ct)
    {
        var screen = await _api.GetEmployeeDevicesScreenAsync(employeeId, ct);
        TempData["LastEmployeeId"] = employeeId.ToString();

        if (screen == null || screen.Employee == null)
        {
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
        var ok = await _api.AssignOneAsync(employeeId, terminalId, ct);
        TempData["ToastType"] = ok ? "success" : "danger";
        TempData["ToastMsg"] = ok ? "✅ تم الربط" : "❌ فشل الربط";
        TempData["LastEmployeeId"] = employeeId.ToString();

        var screen = await _api.GetEmployeeDevicesScreenAsync(employeeId, ct);

        if (ok)
        {
            var employeeName = screen?.Employee?.FullNameAr;
            var employeeText = !string.IsNullOrWhiteSpace(employeeName)
                ? $"{employeeName} ({employeeId})"
                : $"رقم {employeeId}";

            await _activity.LogAsync(
                action: "EmployeeTerminal.Assigned",
                entityType: "EmployeeTerminal",
                entityId: terminalId,
                summary: $"تم ربط الموظف {employeeText} بجهاز {terminalId}.",
                details: new { employeeId, employeeName, terminalId },
                ct: ct
            );
        }

        return View("Search", screen);
    }

    [HttpPost("unassign")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Unassign(int employeeId, string terminalId, CancellationToken ct)
    {
        var ok = await _api.UnassignOneAsync(employeeId, terminalId, ct);
        TempData["ToastType"] = ok ? "success" : "danger";
        TempData["ToastMsg"] = ok ? "✅ تم فك الارتباط" : "❌ فشل فك الارتباط";
        TempData["LastEmployeeId"] = employeeId.ToString();

        var screen = await _api.GetEmployeeDevicesScreenAsync(employeeId, ct);

        if (ok)
        {
            var employeeName = screen?.Employee?.FullNameAr;
            var employeeText = !string.IsNullOrWhiteSpace(employeeName)
                ? $"{employeeName} ({employeeId})"
                : $"رقم {employeeId}";

            await _activity.LogAsync(
                action: "EmployeeTerminal.Unassigned",
                entityType: "EmployeeTerminal",
                entityId: terminalId,
                summary: $"تم فك ربط الموظف {employeeText} من جهاز {terminalId}.",
                details: new { employeeId, employeeName, terminalId },
                ct: ct
            );
        }

        return View("Search", screen);
    }

    [HttpPost("AssignBulk")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AssignBulk(int employeeId, List<string> terminalIds, CancellationToken ct)
    {
        var successCount = 0;
        foreach (var terminalId in terminalIds)
        {
            if (await _api.AssignOneAsync(employeeId, terminalId, ct)) successCount++;
        }

        TempData["ToastType"] = "success";
        TempData["ToastMsg"] = $"✅ تم ربط {terminalIds.Count} جهاز";

        var screen = await _api.GetEmployeeDevicesScreenAsync(employeeId, ct);

        if (successCount > 0)
        {
            var employeeName = screen?.Employee?.FullNameAr;
            var employeeText = !string.IsNullOrWhiteSpace(employeeName)
                ? $"{employeeName} ({employeeId})"
                : $"رقم {employeeId}";

            await _activity.LogAsync(
                action: "EmployeeTerminal.BulkAssigned",
                entityType: "EmployeeTerminal",
                entityId: null,
                summary: $"تم ربط الموظف {employeeText} بعدد {successCount} أجهزة.",
                details: new { employeeId, employeeName, requested = terminalIds.Count, succeeded = successCount },
                ct: ct
            );
        }

        return View("Search", screen);
    }

    [HttpPost("UnassignBulk")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UnassignBulk(int employeeId, List<string> terminalIds, CancellationToken ct)
    {
        var successCount = 0;
        foreach (var terminalId in terminalIds)
        {
            if (await _api.UnassignOneAsync(employeeId, terminalId, ct)) successCount++;
        }

        TempData["ToastType"] = "success";
        TempData["ToastMsg"] = $"✅ تم فك الارتباط عن {terminalIds.Count} جهاز";

        var screen = await _api.GetEmployeeDevicesScreenAsync(employeeId, ct);

        if (successCount > 0)
        {
            var employeeName = screen?.Employee?.FullNameAr;
            var employeeText = !string.IsNullOrWhiteSpace(employeeName)
                ? $"{employeeName} ({employeeId})"
                : $"رقم {employeeId}";

            await _activity.LogAsync(
                action: "EmployeeTerminal.BulkUnassigned",
                entityType: "EmployeeTerminal",
                entityId: null,
                summary: $"تم فك ربط الموظف {employeeText} من عدد {successCount} أجهزة.",
                details: new { employeeId, employeeName, requested = terminalIds.Count, succeeded = successCount },
                ct: ct
            );
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
        var result = await _delegations.SaveDelegationAsync(employeeId, terminalIds, startDate, endDate, ct);
        if (result == "Invalid") { TempData["ToastType"] = "danger"; TempData["ToastMsg"] = "❌ فشل في حفظ الانتداب، تحقق من التواريخ"; }
        else { TempData["ToastType"] = "success"; TempData["ToastMsg"] = "✅ تم جدولة الانتداب بنجاح"; }
        var screen = await _api.GetEmployeeDevicesScreenAsync(employeeId, ct);
        return View("Search", screen);
    }
}

