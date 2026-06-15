using BioAccess.Web.External;
using BioAccess.Web.Services.Monitoring;
using Microsoft.AspNetCore.Mvc;

namespace BioAccess.Web.Controllers;

public sealed class MonitoringController : Controller
{
    private readonly AlpetaClient _alpeta;
    private readonly SystemMetrics _metrics;

    public MonitoringController(AlpetaClient alpeta, SystemMetrics metrics)
    {
        _alpeta = alpeta;
        _metrics = metrics;
    }

    [HttpGet("/monitoring")]
    public IActionResult Index()
    {
        var isAdmin = HttpContext.Session.GetString("IsAdmin") == "1";
        if (!isAdmin)
        {
            return Redirect("/dashboard");
        }

        return View();
    }

    [HttpGet("/api/health")]
    public async Task<IActionResult> Health(CancellationToken ct)
    {
        var alpetaOk = await _alpeta.PingAsync(ct);

        return Ok(new
        {
            status = "ok",
            time = DateTime.UtcNow,
            alpeta = alpetaOk ? "ok" : "down",
            version = "1.0"
        });
    }

    [HttpGet("/api/metrics")]
    public IActionResult Metrics()
    {
        return Ok(new
        {
            assignSuccess = _metrics.AssignSuccess,
            assignFail = _metrics.AssignFail,
            unassignSuccess = _metrics.UnassignSuccess,
            unassignFail = _metrics.UnassignFail,
            timeoutCount = _metrics.TimeoutCount
        });
    }
}
