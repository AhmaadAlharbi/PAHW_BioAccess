namespace Terminals.Web.DTOs;

public class AttendanceRequestDetailsDto
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public string EmployeeName { get; set; } = "";
    public DateTime AttendanceDate { get; set; }
    public int CreatedByEmployeeId { get; set; }
    public string CreatedByName { get; set; } = "";
    public string? HrNote { get; set; }
    public string? ITNote { get; set; }
    public int? AnsweredByEmployeeId { get; set; }
    public string? AnsweredByName { get; set; }
    public DateTime? AnsweredAt { get; set; }
    public string Status { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public List<AttendanceRequestItemDetailsDto> Items { get; set; } = new();
    public List<AttendanceRequestAttachmentDetailsDto> Attachments { get; set; } = new();
    public Dictionary<int, string?> ReplyInputs { get; set; } = new();
    public Dictionary<int, string> ReplyErrors { get; set; } = new();
    public string? ITNoteInput { get; set; }
    public List<string> ValidationMessages { get; set; } = new();

    public static string ToArabicStatus(string status)
        => status switch
        {
            "PendingIT" => "بانتظار رد تقنية المعلومات",
            "Answered" => "تم الرد",
            _ => "غير معروف"
        };

    public static string ToStatusBadgeClass(string status)
        => status switch
        {
            "PendingIT" => "badge badge-warning",
            "Answered" => "badge badge-success",
            _ => "badge badge-muted"
        };

    public static string ToArabicType(string type)
        => type switch
        {
            "mobileIn" => "حضور تطبيق الجوال",
            "fingerprintIn" => "حضور جهاز البصمة",
            "mobileOut" => "انصراف تطبيق الجوال",
            "fingerprintOut" => "انصراف جهاز البصمة",
            _ => "غير معروف"
        };
}

public record AttendanceRequestItemDetailsDto(
    int Id,
    string Type,
    string? Reply
);

public record AttendanceRequestAttachmentDetailsDto(
    int Id,
    string FileName,
    string ContentType,
    DateTime UploadedAt
);
