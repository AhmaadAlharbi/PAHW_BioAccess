namespace Terminals.Web.DTOs;

public class AttendanceRequestListFilterDto
{
    public string? Month { get; set; }
    public string? EmployeeId { get; set; }
    public string? AttendanceDate { get; set; }
    public string? Status { get; set; }
}
