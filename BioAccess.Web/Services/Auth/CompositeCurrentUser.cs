using System.Security.Claims;

namespace BioAccess.Web.Services.Auth;

public sealed class CompositeCurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    public int EmployeeId      => Resolve().EmployeeId;
    public string EmployeeName => Resolve().EmployeeName;
    public bool IsAdmin        => Resolve().IsAdmin;
    public bool IsAuthenticated => Resolve().IsAuthenticated;

    private ICurrentUser Resolve()
    {
        var user = httpContextAccessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated == true && user.FindFirst("emp_id") != null)
            return new JwtCurrentUser(httpContextAccessor);

        return new SessionCurrentUser(httpContextAccessor);
    }
}
