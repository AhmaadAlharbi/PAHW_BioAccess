namespace BioAccess.Web.Services.Auth;

public interface ICurrentUser
{
    int EmployeeId { get; }
    string EmployeeName { get; }
    bool IsAdmin { get; }
    bool IsAuthenticated { get; }
}
