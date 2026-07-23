using Terminals.Web.Contracts;
using Terminals.Web.DTOs;
using Terminals.Web.Services.Activity;
using Terminals.Web.Services.Employees;
using Microsoft.AspNetCore.Mvc;

namespace Terminals.Web.Controllers;

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
            TempData["ToastMsg"] = $"Ù„Ø§ ÙŠÙˆØ¬Ø¯ Ù…ÙˆØ¸Ù Ø¨Ø§Ù„Ø±Ù‚Ù… Ø§Ù„ÙˆØ¸ÙŠÙÙŠ: {employeeId}";
            return View("Search");
        }

        return View("Search", screen);
    }

    [HttpPost("assign")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Assign(int employeeId, string terminalId, CancellationToken ct)
    {
        // Assign one terminal to the employee in Alpeta.
        var ok = await _employees.AssignOneAsync(employeeId, terminalId, ct);
        TempData["ToastType"] = ok ? "success" : "danger";
        TempData["ToastMsg"] = ok ? "âœ… ØªÙ… Ø§Ù„Ø±Ø¨Ø·" : "âŒ ÙØ´Ù„ Ø§Ù„Ø±Ø¨Ø·";
        TempData["LastEmployeeId"] = employeeId.ToString();

        var screen = await _employees.GetEmployeeDevicesScreenAsync(employeeId, ct);
        if (ok)
        {
            // Log only after reloading the screen so the log can include region info.
            var employeeText = FormatEmployeeText(screen?.Employee?.FullNameAr, employeeId);
            var actorText = FormatActorText(HttpContext.Session.GetString("EmpName"), HttpContext.Session.GetString("EmpId"));
            var regionLine = FormatRegionLine(screen?.Devices, new[] { terminalId });
            await _activityLog.LogAsync(
                "EmployeeTerminal.Assigned",
                "EmployeeTerminal",
                terminalId,
                $"ØªÙ… Ø±Ø¨Ø· Ø£Ø¬Ù‡Ø²Ø© Ø¹Ø¯Ø¯Ù‡Ø§ (1) Ù„Ù„Ù…ÙˆØ¸Ù {employeeText}{regionLine}\nØ¨ÙˆØ§Ø³Ø·Ø©: {actorText}");
        }
        return View("Search", screen);
    }

    [HttpPost("unassign")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Unassign(int employeeId, string terminalId, CancellationToken ct)
    {
        // Remove one terminal from the employee in Alpeta.
        var ok = await _employees.UnassignOneAsync(employeeId, terminalId, ct);
        TempData["ToastType"] = ok ? "success" : "danger";
        TempData["ToastMsg"] = ok ? "âœ… ØªÙ… ÙÙƒ Ø§Ù„Ø§Ø±ØªØ¨Ø§Ø·" : "âŒ ÙØ´Ù„ ÙÙƒ Ø§Ù„Ø§Ø±ØªØ¨Ø§Ø·";
        TempData["LastEmployeeId"] = employeeId.ToString();

        var screen = await _employees.GetEmployeeDevicesScreenAsync(employeeId, ct);
        if (ok)
        {
            var employeeText = FormatEmployeeText(screen?.Employee?.FullNameAr, employeeId);
            var actorText = FormatActorText(HttpContext.Session.GetString("EmpName"), HttpContext.Session.GetString("EmpId"));
            var regionLine = FormatRegionLine(screen?.Devices, new[] { terminalId });
            await _activityLog.LogAsync(
                "EmployeeTerminal.Unassigned",
                "EmployeeTerminal",
                terminalId,
                $"ØªÙ… ÙÙƒ Ø±Ø¨Ø· Ø£Ø¬Ù‡Ø²Ø© Ø¹Ø¯Ø¯Ù‡Ø§ (1) Ø¹Ù† Ø§Ù„Ù…ÙˆØ¸Ù {employeeText}{regionLine}\nØ¨ÙˆØ§Ø³Ø·Ø©: {actorText}");
        }
        return View("Search", screen);
    }

    [HttpPost("AssignBulk")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AssignBulk(int employeeId, List<string> terminalIds, CancellationToken ct)
    {
        // Run bulk assignment one terminal at a time.
        var successCount = 0;
        var successfulTerminalIds = new List<string>();
        foreach (var terminalId in terminalIds)
        {
            if (await _employees.AssignOneAsync(employeeId, terminalId, ct))
            {
                successCount++;
                successfulTerminalIds.Add(terminalId);
            }
        }

        TempData["ToastType"] = "success";
        TempData["ToastMsg"] = $"âœ… ØªÙ… Ø±Ø¨Ø· {successCount} Ø¬Ù‡Ø§Ø²";

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
                $"ØªÙ… Ø±Ø¨Ø· Ø£Ø¬Ù‡Ø²Ø© Ø¹Ø¯Ø¯Ù‡Ø§ ({successCount}) Ù„Ù„Ù…ÙˆØ¸Ù {employeeText}{regionLine}\nØ¨ÙˆØ§Ø³Ø·Ø©: {actorText}");
        }
        return View("Search", screen);
    }

    [HttpPost("UnassignBulk")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UnassignBulk(int employeeId, List<string> terminalIds, CancellationToken ct)
    {
        // Run bulk unassignment one terminal at a time.
        var successCount = 0;
        var successfulTerminalIds = new List<string>();
        foreach (var terminalId in terminalIds)
        {
            if (await _employees.UnassignOneAsync(employeeId, terminalId, ct))
            {
                successCount++;
                successfulTerminalIds.Add(terminalId);
            }
        }

        TempData["ToastType"] = "success";
        TempData["ToastMsg"] = $"âœ… ØªÙ… ÙÙƒ Ø§Ù„Ø§Ø±ØªØ¨Ø§Ø· Ø¹Ù† {successCount} Ø¬Ù‡Ø§Ø²";

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
                $"ØªÙ… ÙÙƒ Ø±Ø¨Ø· Ø£Ø¬Ù‡Ø²Ø© Ø¹Ø¯Ø¯Ù‡Ø§ ({successCount}) Ø¹Ù† Ø§Ù„Ù…ÙˆØ¸Ù {employeeText}{regionLine}\nØ¨ÙˆØ§Ø³Ø·Ø©: {actorText}");
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
            TempData["ToastMsg"] = "âŒ ÙØ´Ù„ ÙÙŠ Ø­ÙØ¸ Ø§Ù„Ø§Ù†ØªØ¯Ø§Ø¨ØŒ ØªØ­Ù‚Ù‚ Ù…Ù† Ø§Ù„ØªÙˆØ§Ø±ÙŠØ®";
        }
        else
        {
            TempData["ToastType"] = "success";
            TempData["ToastMsg"] = "âœ… ØªÙ… Ø¬Ø¯ÙˆÙ„Ø© Ø§Ù„Ø§Ù†ØªØ¯Ø§Ø¨ Ø¨Ù†Ø¬Ø§Ø­";
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
        TempData["ToastMsg"] = ok ? "âœ… ØªÙ… Ø¥Ù†Ù‡Ø§Ø¡ Ø§Ù„Ù†Ø¯Ø¨ Ø¨Ù†Ø¬Ø§Ø­" : "âŒ ØªØ¹Ø°Ø± Ø¥Ù†Ù‡Ø§Ø¡ Ø§Ù„Ù†Ø¯Ø¨";

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
        TempData["ToastMsg"] = ok ? "âœ… ØªÙ… Ø¥Ù„ØºØ§Ø¡ Ø§Ù„Ù†Ø¯Ø¨ Ø¨Ù†Ø¬Ø§Ø­" : "âŒ ØªØ¹Ø°Ø± Ø¥Ù„ØºØ§Ø¡ Ø§Ù„Ù†Ø¯Ø¨";

        var screen = await _employees.GetEmployeeDevicesScreenAsync(employeeId, ct);
        return View("Search", screen);
    }

    private static string FormatEmployeeText(string? employeeName, int employeeId)
        => string.IsNullOrWhiteSpace(employeeName)
            ? $"ØºÙŠØ± Ù…Ø¹Ø±ÙˆÙ ({employeeId})"
            : $"{employeeName.Trim()} ({employeeId})";

    private static string FormatActorText(string? actorName, string? actorId)
    {
        var name = string.IsNullOrWhiteSpace(actorName) ? "ØºÙŠØ± Ù…Ø¹Ø±ÙˆÙ" : actorName.Trim();
        var id = string.IsNullOrWhiteSpace(actorId) ? "ØºÙŠØ± Ù…Ø¹Ø±ÙˆÙ" : actorId.Trim();
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
            : $"\nÙÙŠ Ø§Ù„Ù…Ù†Ø§Ø·Ù‚: {string.Join("ØŒ ", regionNames)}";
    }
}
