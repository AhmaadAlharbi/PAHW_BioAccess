using BioAccess.Web.DTOs.Api;
using BioAccess.Web.External;
using BioAccess.Web.Services.Auth;
using BioAccess.Web.Services.Monitoring;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BioAccess.Web.Controllers;

public sealed class MonitoringController : Controller
{
    private readonly AlpetaClient _alpeta;
    private readonly SystemMetrics _metrics;
    private readonly ICurrentUser _currentUser;

    public MonitoringController(AlpetaClient alpeta, SystemMetrics metrics, ICurrentUser currentUser)
    {
        _alpeta = alpeta;
        _metrics = metrics;
        _currentUser = currentUser;
    }

    [HttpGet("/monitoring")]
    public IActionResult Index()
    {
        if (!_currentUser.IsAdmin)
            return Redirect("/dashboard");

        return View();
    }

    [HttpGet("/api/health")]
    [Authorize(AuthenticationSchemes = "Bearer")]
    public async Task<IActionResult> Health(CancellationToken ct)
    {
        if (!_currentUser.IsAdmin)
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse.Fail("غير مصرح"));

        var alpetaOk = await _alpeta.PingAsync(ct);
        return Ok(ApiResponse<HealthStatusDto>.Ok(
            new HealthStatusDto("ok", DateTime.UtcNow, alpetaOk ? "ok" : "down", "1.0")));
    }

    [HttpGet("/api/metrics")]
    [Authorize(AuthenticationSchemes = "Bearer")]
    public IActionResult Metrics()
    {
        if (!_currentUser.IsAdmin)
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse.Fail("غير مصرح"));

        return Ok(ApiResponse<MetricsDto>.Ok(
            new MetricsDto(
                _metrics.AssignSuccess,
                _metrics.AssignFail,
                _metrics.UnassignSuccess,
                _metrics.UnassignFail,
                _metrics.TimeoutCount)));
    }

    private sealed record HealthStatusDto(string Status, DateTime Time, string Alpeta, string Version);
    private sealed record MetricsDto(int AssignSuccess, int AssignFail, int UnassignSuccess, int UnassignFail, int TimeoutCount);
}
