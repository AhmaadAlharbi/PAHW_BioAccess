namespace BioAccess.Web.DTOs.Api;

public sealed class LoginRequest
{
    public int EmployeeId { get; set; }
    public string Password { get; set; } = "";
}
