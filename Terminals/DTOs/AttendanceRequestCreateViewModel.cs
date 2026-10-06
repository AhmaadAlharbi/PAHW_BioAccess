namespace Terminals.Web.DTOs;

public class AttendanceRequestCreateViewModel
{
    public int? EmployeeId { get; set; }
    public string EmployeeName { get; set; } = "";
    public string Department { get; set; } = "";
    public string JobTitle { get; set; } = "";
    public bool LookupSucceeded { get; set; }
    public string RequestedDate { get; set; } = "";
    public string HrNote { get; set; } = "";
    public List<string> SelectedTypes { get; set; } = new();
    public List<string> ValidationMessages { get; set; } = new();
    public string SuccessMessage { get; set; } = "";

    public bool IsSelected(string type)
        => SelectedTypes.Contains(type);
}
