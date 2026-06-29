namespace BioAccess.Web.Models;

public sealed class ActivityLogRowViewModel
{
    public string TimeText { get; set; } = "";
    public string ActorText { get; set; } = "";
    public string ActionText { get; set; } = "";
    public string Summary { get; set; } = "";
}

public sealed class DelegationRowViewModel
{
    public int EmployeeId { get; set; }
    public string EmployeeText { get; set; } = "";
    public string? RegionText { get; set; }
    public bool IsCurrentUser { get; set; }

    public string StatusText { get; set; } = "";
    public string StatusBadgeClass { get; set; } = "badge badge-muted";

    public string StartDateText { get; set; } = "";
    public string EndDateText { get; set; } = "";

    public int TerminalsCount { get; set; }
    public string TerminalsCountText { get; set; } = "";

    public string? DevicesHintText { get; set; }
}

public sealed class DashboardViewModel
{
    public bool IsAdmin { get; set; }
    public int CurrentUserEmpId { get; set; }

    public int RegionsCount { get; set; }
    public int MappingsCount { get; set; }
    public int ActiveDelegationsCount { get; set; }

    public List<ActivityLogRowViewModel> LatestActivity { get; set; } = new();
    public List<DelegationRowViewModel> LatestDelegations { get; set; } = new();

    // Active | Scheduled | All
    public string DelegationsFilter { get; set; } = "Active";

    public static string ToArabicDelegationStatus(string status)
        => status switch
        {
            "Active" => "نشط",
            "Scheduled" => "مجدول",
            "Expired" => "منتهي",
            "Cancelled" => "ملغي",
            "ManuallyEnded" => "ملغي",
            _ => "—"
        };

    public static string ToDelegationStatusBadgeClass(string status)
        => status switch
        {
            "Active" => "badge badge-success",
            "Scheduled" => "badge badge-info",
            "Expired" => "badge badge-muted",
            "Cancelled" => "badge badge-muted",
            "ManuallyEnded" => "badge badge-muted",
            _ => "badge badge-muted"
        };

    public static string ToArabicAction(string action)
        => action switch
        {
            "Delegation.Created" => "إنشاء انتداب",
            "Delegation.Activated" => "تفعيل انتداب",
            "Delegation.Expired" => "انتهاء انتداب",
            "Delegation.ManuallyEnded" => "إنهاء ندب",
            "Delegation.Cancelled" => "إلغاء ندب",

            "TerminalRegion.Assigned" => "ربط جهاز بمنطقة",
            "TerminalRegion.Moved" => "نقل جهاز لمنطقة أخرى",
            "TerminalRegion.Cleared" => "فك ربط جهاز",
            "TerminalRegion.BulkAssigned" => "توزيع أجهزة (جماعي)",
            "TerminalRegion.BulkCleared" => "فك ربط أجهزة (جماعي)",
            "TerminalRegion.AutoAssign" => "توزيع تلقائي للمناطق",

            "EmployeeTerminal.Assigned" => "ربط موظف بجهاز",
            "EmployeeTerminal.Unassigned" => "فك ربط موظف من جهاز",
            "EmployeeTerminal.BulkAssigned" => "ربط الموظف بعدة أجهزة",
            "EmployeeTerminal.BulkUnassigned" => "فك ربط الموظف من عدة أجهزة",

            "Region.Created" => "إنشاء منطقة",
            "Region.Renamed" => "تعديل اسم منطقة",
            "Region.Deleted" => "حذف منطقة",

            "AllowedUser.Added" => "إضافة مستخدم مسموح",
            "AllowedUser.Activated" => "تفعيل مستخدم",
            "AllowedUser.Deactivated" => "تعطيل مستخدم",
            "AllowedUser.Deleted" => "حذف مستخدم",

            _ => "عملية"
        };
}
