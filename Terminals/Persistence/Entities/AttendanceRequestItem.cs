namespace Terminals.Web.Persistence.Entities;

public class AttendanceRequestItem
{
    public int Id { get; set; }
    public int AttendanceRequestId { get; set; }
    public string Type { get; set; } = "";
    public string? Reply { get; set; }

    public AttendanceRequest? AttendanceRequest { get; set; }
}
