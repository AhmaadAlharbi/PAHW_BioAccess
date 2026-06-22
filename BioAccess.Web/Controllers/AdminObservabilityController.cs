using BioAccess.Web.External;
using BioAccess.Web.Services.Observability;
using BioAccess.Web.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BioAccess.Web.Controllers;

public sealed class AdminObservabilityController : Controller
{
    private readonly DeviceObservabilityService _observability;
    private readonly ILogger<AdminObservabilityController> _logger;

    public AdminObservabilityController(
        DeviceObservabilityService observability,
        ILogger<AdminObservabilityController> logger)
    {
        _observability = observability;
        _logger = logger;
    }

    public override void OnActionExecuting(ActionExecutingContext context)
    {
        var isAdmin = HttpContext.Session.GetString("IsAdmin") == "1";
        if (!isAdmin)
        {
            HttpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Result = View("Forbidden");
            return;
        }

        base.OnActionExecuting(context);
    }

    [HttpGet("/admin/observability/devices")]
    public async Task<IActionResult> Devices(CancellationToken ct)
    {
        DevicesObservabilityViewModel model;

        try
        {
            model = await _observability.GetDevicesAsync(ct);
            _logger.LogInformation(
                "OBS_CONTROLLER_MODEL devices={Devices} activeToday={ActiveToday} activeWeek={ActiveWeek} noActivity={NoActivity} error={Error}",
                model.Devices.Count,
                model.ActiveTodayDevices,
                model.ActiveThisWeekDevices,
                model.NoActivityDevices,
                model.Error ?? "<none>");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ADMIN_OBSERVABILITY_DEVICES_LOAD_FAILED");
            model = new DevicesObservabilityViewModel
            {
                Error = "تعذر تحميل لوحة صحة الأجهزة حالياً."
            };
        }

        return View("~/Views/Admin/Observability/Devices.cshtml", model);
    }
}
