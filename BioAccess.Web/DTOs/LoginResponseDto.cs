namespace BioAccess.Web.DTOs;

public class LoginResponseDto
{
    public int ResultCode { get; set; }          // 1 = success, 0 = fail
    public string Message { get; set; } = "";
    public string SessionKey { get; set; } = "";
    public string EmployeeName { get; set; } = "";
}
