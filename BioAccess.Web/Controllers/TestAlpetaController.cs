using BioAccess.Web.External;
using Microsoft.AspNetCore.Mvc;

namespace BioAccess.Web.Controllers;

[ApiController]
[Route("api/test/alpeta")]
public sealed class TestAlpetaController : ControllerBase
{
    private readonly AlpetaClient _alpeta;
    private readonly IConfiguration _config;

    public TestAlpetaController(AlpetaClient alpeta, IConfiguration config)
    {
        _alpeta = alpeta;
        _config = config;
    }

    [HttpGet("ping")]
    public IActionResult Ping()
    {
        return Ok(new { status = "ok" });
    }

    [HttpPost("assign-test")]
    public async Task<IActionResult> AssignTest(CancellationToken ct)
    {
        var terminalId = _config["Testing:Alpeta:TerminalId"];
        var employeeId = ParseEmployeeId(_config["Testing:Alpeta:EmployeeId"]);

        if (string.IsNullOrWhiteSpace(terminalId) || employeeId <= 0)
            return BadRequest(new { status = "not_configured" });

        var result = await _alpeta.AssignUserToTerminalAsync(terminalId, employeeId, ct);
        return Ok(new { success = result.Success, message = result.Message, terminalId, employeeId });
    }

    [HttpPost("unassign-test")]
    public async Task<IActionResult> UnassignTest(CancellationToken ct)
    {
        var terminalId = _config["Testing:Alpeta:TerminalId"];
        var employeeId = ParseEmployeeId(_config["Testing:Alpeta:EmployeeId"]);

        if (string.IsNullOrWhiteSpace(terminalId) || employeeId <= 0)
            return BadRequest(new { status = "not_configured" });

        var result = await _alpeta.UnassignUserFromTerminalAsync(terminalId, employeeId, ct);
        return Ok(new { success = result.Success, message = result.Message, terminalId, employeeId });
    }

    private static int ParseEmployeeId(string? value)
        => int.TryParse(value, out var employeeId) ? employeeId : 0;
}
