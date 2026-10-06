namespace Terminals.Web.Persistence.Entities;

public class AttendanceRequest
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public string EmployeeName { get; set; } = "";
    public DateTime AttendanceDate { get; set; }
    public int CreatedByEmployeeId { get; set; }
    public string CreatedByName { get; set; } = "";
    public string? HrNote { get; set; }
    public string Status { get; set; } = "PendingIT";
    public string? ITNote { get; set; }
    public int? AnsweredByEmployeeId { get; set; }
    public string? AnsweredByName { get; set; }
    public DateTime? AnsweredAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public List<AttendanceRequestItem> Items { get; set; } = new();
    public List<AttendanceRequestAttachment> Attachments { get; set; } = new();
}
