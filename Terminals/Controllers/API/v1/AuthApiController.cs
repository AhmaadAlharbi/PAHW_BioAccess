using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Terminals.Web.Contracts;
using Terminals.Web.DTOs.Api;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;

namespace Terminals.Web.Controllers.API.v1;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthApiController(
    ILoginApi loginApi,
    IAllowedUsersStore allowedUsers,
    IConfiguration config,
    ILogger<AuthApiController> logger) : ControllerBase
{
    private readonly ILogger<AuthApiController> _logger = logger;
    private const int ExpiresInSeconds = 3600;

    [HttpPost("token")]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Token([FromBody] LoginRequest request, CancellationToken ct)
    {
        var loginResult = await loginApi.LoginAsync(request.EmployeeId.ToString(), request.Password, ct);

        if (loginResult.ResultCode != 1)
        {
            _logger.LogWarning("Login failed: {Message}", loginResult.Message);
            return Unauthorized(ApiResponse.Fail("بيانات الدخول غير صحيحة"));
        }

        var isAllowed = await allowedUsers.IsAllowedAsync(request.EmployeeId, ct);
        if (!isAllowed)
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse.Fail("غير مصرح لك بالدخول. راجع قسم الإجازات والدوام."));

        var isAdmin = await allowedUsers.IsAdminAsync(request.EmployeeId, ct);
        var token = GenerateToken(request.EmployeeId, loginResult.EmployeeName, isAdmin);

        return Ok(ApiResponse<TokenResponse>.Ok(new TokenResponse(token, ExpiresInSeconds)));
    }

    private string GenerateToken(int empId, string employeeName, bool isAdmin)
    {
        var claims = new[]
        {
            new Claim("emp_id", empId.ToString()),
            new Claim("name", employeeName),
            new Claim(ClaimTypes.Role, isAdmin ? "admin" : "user")
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Secret"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: config["Jwt:Issuer"],
            audience: config["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddSeconds(ExpiresInSeconds),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
