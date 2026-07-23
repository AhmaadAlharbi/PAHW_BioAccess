using Terminals.Web.Contracts;
using Terminals.Web.DTOs;
using Terminals.Web.DTOs.Api;
using Terminals.Web.Services.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Terminals.Web.Controllers.API.v1;

[ApiController]
[Route("api/v1/employees")]
[Authorize(AuthenticationSchemes = "Bearer")]
public sealed class EmployeesApiController(
    IEmployeeDevicesApi employeeDevicesApi,
    ICurrentUser currentUser) : ControllerBase
{
    [HttpGet("{employeeId:int}/devices")]
    public async Task<IActionResult> GetDevices(int employeeId, CancellationToken ct)
    {
        if (!currentUser.IsAdmin && currentUser.EmployeeId != employeeId)
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse.Fail("غير مصرح"));

        var result = await employeeDevicesApi.GetEmployeeDevicesScreenAsync(employeeId, ct);

        if (result is null)
            return NotFound(ApiResponse.Fail("Employee not found"));

        if (!string.IsNullOrEmpty(result.Error))
            return StatusCode(503, ApiResponse.Fail(result.Error));

        return Ok(ApiResponse<EmployeeDevicesScreenDto>.Ok(result));
    }
}
