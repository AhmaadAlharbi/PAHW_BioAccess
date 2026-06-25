namespace BioAccess.Web.ViewModels;

public sealed class FailedUserEntry
{
    public string UserId { get; init; } = "";
    public string UserName { get; init; } = "";
    public int Count { get; init; }
}

public sealed class DeviceHealthItem
{
    public string TerminalId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Ip { get; set; } = "";
    public string Status { get; set; } = "No Activity";
    public int ErrorCount { get; set; }
    public int ActivityCount { get; set; }
    public int FailedAuthCount { get; set; }
    public List<FailedUserEntry> TopFailedUsers { get; set; } = new();
    public DateTime? LastSeen { get; set; }
    public DateTime? LastFailedAt { get; set; }
    public string ProblemReason { get; set; } = "";
    public string ProblemSummary { get; set; } = "";
}

public sealed class DevicesObservabilityViewModel
{
    public int TotalDevices { get; set; }
    public int ActiveTodayDevices { get; set; }
    public int ActiveThisWeekDevices { get; set; }
    public int NoActivityDevices { get; set; }
    public int SecurityAlertDevices { get; set; }
    public List<DeviceHealthItem> Devices { get; set; } = new();
    public string? Error { get; set; }
}
