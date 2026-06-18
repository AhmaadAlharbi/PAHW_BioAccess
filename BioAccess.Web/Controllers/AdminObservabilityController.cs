using BioAccess.Web.External;
using BioAccess.Web.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BioAccess.Web.Controllers;

public sealed class AdminObservabilityController : Controller
{
    private readonly AlpetaClient _alpeta;
    private readonly ILogger<AdminObservabilityController> _logger;

    public AdminObservabilityController(
        AlpetaClient alpeta,
        ILogger<AdminObservabilityController> logger)
    {
        _alpeta = alpeta;
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
        var model = new DevicesObservabilityViewModel();

        try
        {
            var terminals = await _alpeta.GetAllDevicesAsync(ct);

            model.Devices = terminals
                .Where(x => !string.IsNullOrWhiteSpace(x.DeviceId))
                .Select(x =>
                {
                    var terminalId = x.DeviceId.Trim();

                    return new DeviceHealthItem
                    {
                        TerminalId = terminalId,
                        Name = string.IsNullOrWhiteSpace(x.DeviceName) ? $"Terminal {terminalId}" : x.DeviceName.Trim(),
                        Ip = x.IPAddress?.Trim() ?? "",
                        Status = x.IsOnline ? "Healthy" : "Warning",
                        ErrorCount = 0,
                        ActivityCount = 0,
                        LastSeen = null,
                        ProblemSummary = x.IsOnline
                            ? "No significant issues."
                            : "Observability logs are currently unavailable."
                    };
                })
                .OrderBy(x => GetStatusRank(x.Status))
                .ThenByDescending(x => x.ErrorCount)
                .ThenBy(x => x.ActivityCount == 0 ? 0 : 1)
                .ThenBy(x => x.Name)
                .ThenBy(x => x.TerminalId)
                .ToList();

            model.TotalDevices = model.Devices.Count;
            model.HealthyDevices = model.Devices.Count(x => x.Status == "Healthy");
            model.WarningDevices = model.Devices.Count(x => x.Status == "Warning");
            model.ProblemDevices = model.Devices.Count(x => x.Status == "Problem");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ADMIN_OBSERVABILITY_DEVICES_LOAD_FAILED");
            model.Error = "تعذر تحميل لوحة صحة الأجهزة حالياً.";
        }

        return View("~/Views/Admin/Observability/Devices.cshtml", model);
    }

    private static string GetStatus(int errorCount, int activityCount)
    {
        if (errorCount >= 3)
            return "Problem";

        if (activityCount == 0)
            return "Warning";

        return "Healthy";
    }

    private static int GetStatusRank(string status) => status switch
    {
        "Problem" => 0,
        "Warning" => 1,
        _ => 2
    };
}
