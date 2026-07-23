using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Http;
using Terminals.Web.Services.Activity;
using Terminals.Web.Services.Terminals;
using Terminals.Web.ViewModels;

namespace Terminals.Web.Controllers;

public class TerminalsController : Controller
{
    private readonly TerminalService _service;
    private readonly IActivityLogService _activityLog;

    public TerminalsController(TerminalService service, IActivityLogService activityLog)
    {
        _service = service;
        _activityLog = activityLog;
    }

    public override void OnActionExecuting(ActionExecutingContext context)
    {
        // Admin-only page (uses the same Session flag set in HomeController).
        var isAdmin = HttpContext.Session.GetString("IsAdmin") == "1";
        if (!isAdmin)
        {
            // For MVC pages, show the existing Forbidden view instead of the default Forbid() response.
            HttpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Result = View("Forbidden");
            return;
        }

        base.OnActionExecuting(context);
    }

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var model = await _service.GetTerminalsViewModelAsync(ct);
        return View(model);
    }

    [HttpGet]
    public IActionResult CreateRegion()
    {
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateRegion(string name, CancellationToken ct)
    {
        try
        {
            await _service.CreateRegionAsync(name, ct);
            await _activityLog.LogAsync(
                "Region.Created",
                "Region",
                null,
                $"تمت إضافة منطقة جديدة باسم: {name}.");
            TempData["ToastType"] = "success";
            TempData["ToastMsg"] = "تمت إضافة المنطقة.";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(nameof(name), ex.Message);
            return View();
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RenameRegion(int regionId, string name, CancellationToken ct)
    {
        try
        {
            await _service.RenameRegionAsync(regionId, name, ct);
            await _activityLog.LogAsync(
                "Region.Renamed",
                "Region",
                regionId.ToString(),
                $"تم تعديل اسم المنطقة رقم {regionId} إلى: {name}.");
            TempData["ToastType"] = "success";
            TempData["ToastMsg"] = "تم تعديل اسم المنطقة.";
        }
        catch (Exception ex)
        {
            TempData["ToastType"] = "danger";
            TempData["ToastMsg"] = ex.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteRegion(int regionId, CancellationToken ct)
    {
        try
        {
            await _service.DeleteRegionAsync(regionId, ct);
            await _activityLog.LogAsync(
                "Region.Deleted",
                "Region",
                regionId.ToString(),
                $"تم حذف المنطقة رقم {regionId}.");
            TempData["ToastType"] = "success";
            TempData["ToastMsg"] = "تم حذف المنطقة.";
        }
        catch (Exception ex)
        {
            TempData["ToastType"] = "danger";
            TempData["ToastMsg"] = ex.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AssignRegion(string terminalId, int regionId, CancellationToken ct)
    {
        try
        {
            await _service.AssignRegionAsync(terminalId, regionId, ct);
            await _activityLog.LogAsync(
                "TerminalRegion.Assigned",
                "TerminalRegionMap",
                terminalId,
                $"تم ربط الجهاز {terminalId} بالمنطقة رقم {regionId}.");
            TempData["ToastType"] = "success";
            TempData["ToastMsg"] = $"تم حفظ منطقة الجهاز {terminalId}.";
        }
        catch (Exception ex)
        {
            TempData["ToastType"] = "danger";
            TempData["ToastMsg"] = ex.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ClearRegion(string terminalId, CancellationToken ct)
    {
        try
        {
            await _service.ClearRegionAsync(terminalId, ct);
            await _activityLog.LogAsync(
                "TerminalRegion.Cleared",
                "TerminalRegionMap",
                terminalId,
                $"تم فك ربط الجهاز {terminalId} من المنطقة.");
            TempData["ToastType"] = "success";
            TempData["ToastMsg"] = $"تم فك ربط الجهاز {terminalId}.";
        }
        catch (Exception ex)
        {
            TempData["ToastType"] = "danger";
            TempData["ToastMsg"] = ex.Message;
        }

        return RedirectToAction(nameof(Index));
    }
}
