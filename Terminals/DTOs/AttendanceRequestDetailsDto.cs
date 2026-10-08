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
    public string Status { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public List<AttendanceRequestItemDetailsDto> Items { get; set; } = new();

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
    string Type,
    string? Reply
);
