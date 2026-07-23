using Terminals.Web.Contracts;
using Terminals.Web.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Terminals.Web.Services.Activity;

public class AllowedUsersController : Controller
{
    private readonly IAllowedUsersAdmin _admin;
    private readonly IActivityLogService _activity;

    public AllowedUsersController(IAllowedUsersAdmin admin, IActivityLogService activity)
    {
        _admin = admin;
        _activity = activity;
    }
    
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        var isAdmin = HttpContext.Session.GetString("IsAdmin") == "1";

        if (!isAdmin)
        {
            context.Result = Forbid();
            return;
        }

        base.OnActionExecuting(context);
    }

    [HttpGet]
    public IActionResult Create()
    {
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Fetch(int employeeId, CancellationToken ct)
    {
        var dto = await _admin.FetchFromSoapAsync(employeeId, ct);
        if (dto == null)
        {
            TempData["ErrorMsg"] = "Ù„Ù… ÙŠØªÙ… Ø§Ù„Ø¹Ø«ÙˆØ± Ø¹Ù„Ù‰ Ø§Ù„Ù…ÙˆØ¸Ù ÙÙŠ SOAP.";
            return RedirectToAction("Create");
        }

        return View("Create", dto);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(int employeeId, string fullName, string email, string department,
        DateTime? validUntil, bool isAdmin, CancellationToken ct)
    {
        var ok = await _admin.AddAsync(new AllowedUserDto(employeeId, fullName, email, department), validUntil, isAdmin, ct);

        TempData["SuccessMsg"] = ok ? "ØªÙ…Øª Ø§Ù„Ø¥Ø¶Ø§ÙØ©." : "Ø§Ù„Ù…ÙˆØ¸Ù Ù…ÙˆØ¬ÙˆØ¯ Ù…Ø³Ø¨Ù‚Ù‹Ø§.";

        if (ok)
        {
            await _activity.LogAsync(
                action: "AllowedUser.Added",
                entityType: "AllowedUser",
                entityId: employeeId.ToString(),
                summary: $"ØªÙ…Øª Ø¥Ø¶Ø§ÙØ© Ù…Ø³ØªØ®Ø¯Ù… Ø¥Ù„Ù‰ Ù‚Ø§Ø¦Ù…Ø© Ø§Ù„ØµÙ„Ø§Ø­ÙŠØ§Øª: {employeeId} ({fullName}).",
                details: new { employeeId, fullName, isAdmin, validUntil },
                ct: ct
            );
        }

        return RedirectToAction("Create");
    }
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var list = await _admin.ListAsync(ct);
        return View(list);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Toggle(int employeeId, bool makeActive, CancellationToken ct)
    {
        if (!int.TryParse(HttpContext.Session.GetString("EmpId"), out var currentEmpId))
        {
            TempData["ErrorMsg"] = "Ø­Ø¯Ø« Ø®Ø·Ø£ ÙÙŠ Ø§Ù„Ø¬Ù„Ø³Ø©.";
            return RedirectToAction("Index");
        }

        // âœ… Ù„Ø§ ØªØ¹Ø·Ù‘Ù„ Ù†ÙØ³Ùƒ
        if (!makeActive && employeeId == currentEmpId)
        {
            TempData["ErrorMsg"] = "Ù„Ø§ ÙŠÙ…ÙƒÙ†Ùƒ ØªØ¹Ø·ÙŠÙ„ Ù†ÙØ³Ùƒ.";
            return RedirectToAction("Index");
        }

        var ok = await _admin.SetActiveAsync(employeeId, makeActive, ct);

        if (!ok)
            TempData["ErrorMsg"] = "Ù„Ø§ ÙŠÙ…ÙƒÙ† ØªÙ†ÙÙŠØ° Ø§Ù„Ø¹Ù…Ù„ÙŠØ© (Ù‚Ø¯ ÙŠÙƒÙˆÙ† Ø¢Ø®Ø± Ù…Ø´Ø±Ù).";
        else
            TempData["SuccessMsg"] = "ØªÙ… ØªØ­Ø¯ÙŠØ« Ø§Ù„Ø­Ø§Ù„Ø© Ø¨Ù†Ø¬Ø§Ø­.";

        if (ok)
        {
            await _activity.LogAsync(
                action: makeActive ? "AllowedUser.Activated" : "AllowedUser.Deactivated",
                entityType: "AllowedUser",
                entityId: employeeId.ToString(),
                summary: makeActive
                    ? $"ØªÙ… ØªÙØ¹ÙŠÙ„ Ø§Ù„Ù…Ø³ØªØ®Ø¯Ù… ÙÙŠ Ù‚Ø§Ø¦Ù…Ø© Ø§Ù„ØµÙ„Ø§Ø­ÙŠØ§Øª: {employeeId}."
                    : $"ØªÙ… ØªØ¹Ø·ÙŠÙ„ Ø§Ù„Ù…Ø³ØªØ®Ø¯Ù… ÙÙŠ Ù‚Ø§Ø¦Ù…Ø© Ø§Ù„ØµÙ„Ø§Ø­ÙŠØ§Øª: {employeeId}.",
                details: new { employeeId, makeActive },
                ct: ct
            );
        }

        return RedirectToAction("Index");
    }


    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int employeeId, CancellationToken ct)
    {
        if (!int.TryParse(HttpContext.Session.GetString("EmpId"), out var currentEmpId))
        {
            TempData["ErrorMsg"] = "Ø­Ø¯Ø« Ø®Ø·Ø£ ÙÙŠ Ø§Ù„Ø¬Ù„Ø³Ø©.";
            return RedirectToAction("Index");
        }

        // âœ… Ù„Ø§ ØªØ³Ù…Ø­ Ù„Ù†ÙØ³Ùƒ ØªØ­Ø°Ù Ù†ÙØ³Ùƒ
        if (employeeId == currentEmpId)
        {
            TempData["ErrorMsg"] = "Ù„Ø§ ÙŠÙ…ÙƒÙ†Ùƒ Ø­Ø°Ù Ù†ÙØ³Ùƒ.";
            return RedirectToAction("Index");
        }

        var ok = await _admin.DeleteAsync(employeeId, ct);

        TempData[ok ? "SuccessMsg" : "ErrorMsg"] = ok
            ? "ØªÙ… Ø­Ø°Ù Ø§Ù„Ù…Ø³ØªØ®Ø¯Ù…."
            : "Ù„Ø§ ÙŠÙ…ÙƒÙ† Ø­Ø°Ù Ù‡Ø°Ø§ Ø§Ù„Ù…Ø³ØªØ®Ø¯Ù… (Ù‚Ø¯ ÙŠÙƒÙˆÙ† Ø¢Ø®Ø± Ù…Ø´Ø±Ù).";

        if (ok)
        {
            await _activity.LogAsync(
                action: "AllowedUser.Deleted",
                entityType: "AllowedUser",
                entityId: employeeId.ToString(),
                summary: $"ØªÙ… Ø­Ø°Ù Ø§Ù„Ù…Ø³ØªØ®Ø¯Ù… Ù…Ù† Ù‚Ø§Ø¦Ù…Ø© Ø§Ù„ØµÙ„Ø§Ø­ÙŠØ§Øª: {employeeId}.",
                details: new { employeeId },
                ct: ct
            );
        }

        return RedirectToAction("Index");
    }



}
