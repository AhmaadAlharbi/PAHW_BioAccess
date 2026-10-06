namespace Terminals.Web.DTOs;

public class CreateAttendanceRequestDto
{
    public int EmployeeId { get; set; }
    public string EmployeeName { get; set; } = "";
    public DateTime AttendanceDate { get; set; }
    public string? HrNote { get; set; }
    public int CreatedByEmployeeId { get; set; }
    public string CreatedByName { get; set; } = "";
    public List<string> Types { get; set; } = new();
}
