namespace Terminals.Web.Models;

public sealed class ActivityLogRowViewModel
{
    public string TimeText { get; set; } = "";
    public string ActorText { get; set; } = "";
    public string ActionText { get; set; } = "";
    public string Summary { get; set; } = "";
}

public sealed class DelegationRowViewModel
{
    public string EmployeeText { get; set; } = "";
    public string? RegionText { get; set; }

    public string StatusText { get; set; } = "";
    public string StatusBadgeClass { get; set; } = "badge badge-muted";

    public string StartDateText { get; set; } = "";
    public string EndDateText { get; set; } = "";

    public int TerminalsCount { get; set; }
    public string TerminalsCountText { get; set; } = "";

    // Optional small extra text under count (regions, or terminal ids fallback)
    public string? DevicesHintText { get; set; }
}

public sealed class DashboardViewModel
{
    public bool IsAdmin { get; set; }

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
            "Active" => "Ù†Ø´Ø·",
            "Scheduled" => "Ù…Ø¬Ø¯ÙˆÙ„",
            "Expired" => "Ù…Ù†ØªÙ‡ÙŠ",
            "Cancelled" => "Ù…Ù„ØºÙŠ",
            "ManuallyEnded" => "Ù…Ù„ØºÙŠ",
            _ => "â€”"
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
            "Delegation.Created" => "Ø¥Ù†Ø´Ø§Ø¡ Ø§Ù†ØªØ¯Ø§Ø¨",
            "Delegation.Activated" => "ØªÙØ¹ÙŠÙ„ Ø§Ù†ØªØ¯Ø§Ø¨",
            "Delegation.Expired" => "Ø§Ù†ØªÙ‡Ø§Ø¡ Ø§Ù†ØªØ¯Ø§Ø¨",
            "Delegation.ManuallyEnded" => "Ø¥Ù†Ù‡Ø§Ø¡ Ù†Ø¯Ø¨",
            "Delegation.Cancelled" => "Ø¥Ù„ØºØ§Ø¡ Ù†Ø¯Ø¨",

            "TerminalRegion.Assigned" => "Ø±Ø¨Ø· Ø¬Ù‡Ø§Ø² Ø¨Ù…Ù†Ø·Ù‚Ø©",
            "TerminalRegion.Moved" => "Ù†Ù‚Ù„ Ø¬Ù‡Ø§Ø² Ù„Ù…Ù†Ø·Ù‚Ø© Ø£Ø®Ø±Ù‰",
            "TerminalRegion.Cleared" => "ÙÙƒ Ø±Ø¨Ø· Ø¬Ù‡Ø§Ø²",
            "TerminalRegion.BulkAssigned" => "ØªÙˆØ²ÙŠØ¹ Ø£Ø¬Ù‡Ø²Ø© (Ø¬Ù…Ø§Ø¹ÙŠ)",
            "TerminalRegion.BulkCleared" => "ÙÙƒ Ø±Ø¨Ø· Ø£Ø¬Ù‡Ø²Ø© (Ø¬Ù…Ø§Ø¹ÙŠ)",
            "TerminalRegion.AutoAssign" => "ØªÙˆØ²ÙŠØ¹ ØªÙ„Ù‚Ø§Ø¦ÙŠ Ù„Ù„Ù…Ù†Ø§Ø·Ù‚",

            "EmployeeTerminal.Assigned" => "Ø±Ø¨Ø· Ù…ÙˆØ¸Ù Ø¨Ø¬Ù‡Ø§Ø²",
            "EmployeeTerminal.Unassigned" => "ÙÙƒ Ø±Ø¨Ø· Ù…ÙˆØ¸Ù Ù…Ù† Ø¬Ù‡Ø§Ø²",
            "EmployeeTerminal.BulkAssigned" => "Ø±Ø¨Ø· Ø§Ù„Ù…ÙˆØ¸Ù Ø¨Ø¹Ø¯Ø© Ø£Ø¬Ù‡Ø²Ø©",
            "EmployeeTerminal.BulkUnassigned" => "ÙÙƒ Ø±Ø¨Ø· Ø§Ù„Ù…ÙˆØ¸Ù Ù…Ù† Ø¹Ø¯Ø© Ø£Ø¬Ù‡Ø²Ø©",

            "Region.Created" => "Ø¥Ù†Ø´Ø§Ø¡ Ù…Ù†Ø·Ù‚Ø©",
            "Region.Renamed" => "ØªØ¹Ø¯ÙŠÙ„ Ø§Ø³Ù… Ù…Ù†Ø·Ù‚Ø©",
            "Region.Deleted" => "Ø­Ø°Ù Ù…Ù†Ø·Ù‚Ø©",

            "AllowedUser.Added" => "Ø¥Ø¶Ø§ÙØ© Ù…Ø³ØªØ®Ø¯Ù… Ù…Ø³Ù…ÙˆØ­",
            "AllowedUser.Activated" => "ØªÙØ¹ÙŠÙ„ Ù…Ø³ØªØ®Ø¯Ù…",
            "AllowedUser.Deactivated" => "ØªØ¹Ø·ÙŠÙ„ Ù…Ø³ØªØ®Ø¯Ù…",
            "AllowedUser.Deleted" => "Ø­Ø°Ù Ù…Ø³ØªØ®Ø¯Ù…",

            _ => "Ø¹Ù…Ù„ÙŠØ©"
        };
}
