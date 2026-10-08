namespace Terminals.Web.DTOs;

public class AttendanceRequestsIndexViewModel
{
    public AttendanceRequestListFilterDto Filter { get; set; } = new();
    public List<AttendanceRequestListItemDto> Requests { get; set; } = new();

    public int TotalCount => Requests.Count;
    public int PendingCount => Requests.Count(x => x.Status == "PendingIT");
    public int AnsweredCount => Requests.Count(x => x.Status == "Answered");

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
}
