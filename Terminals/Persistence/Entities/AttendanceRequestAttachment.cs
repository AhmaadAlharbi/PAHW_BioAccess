namespace Terminals.Web.Persistence.Entities;

public class AttendanceRequestAttachment
{
    public int Id { get; set; }
    public int AttendanceRequestId { get; set; }
    public string FileName { get; set; } = "";
    public string StoredFileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public DateTime UploadedAt { get; set; } = DateTime.Now;

    public AttendanceRequest? AttendanceRequest { get; set; }
}
