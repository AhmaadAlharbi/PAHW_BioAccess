namespace BioAccess.Web.Services.Auth;

public sealed class SessionCurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    private ISession Session => httpContextAccessor.HttpContext!.Session;

    public int EmployeeId =>
        int.TryParse(Session.GetString("EmpId"), out var id) ? id : 0;

    public string EmployeeName => Session.GetString("EmpName") ?? "";

    public bool IsAdmin => Session.GetString("IsAdmin") == "1";

    public bool IsAuthenticated => Session.GetString("EmpId") is not null;
}
