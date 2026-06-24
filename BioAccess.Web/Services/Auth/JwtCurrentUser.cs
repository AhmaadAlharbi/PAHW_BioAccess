using System.Security.Claims;

namespace BioAccess.Web.Services.Auth;

public sealed class JwtCurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    private ClaimsPrincipal User => httpContextAccessor.HttpContext!.User;

    public int EmployeeId =>
        int.TryParse(User.FindFirstValue("emp_id"), out var id) ? id : 0;

    public string EmployeeName => User.FindFirstValue("name") ?? "";

    public bool IsAdmin => User.FindFirstValue("role") == "admin";

    public bool IsAuthenticated => User.Identity?.IsAuthenticated ?? false;
}
