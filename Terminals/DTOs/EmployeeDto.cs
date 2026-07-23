namespace Terminals.Web.DTOs;

public class EmployeeDto
{
    public int EmployeeId { get; set; }                 // الرقم الوظيفي
    public string? CivilId { get; set; }                // نخليه اختياري للمستقبل
    public string FullNameAr { get; set; } = string.Empty;
    public string? Department { get; set; }
    public string? JobTitle { get; set; }
}
