namespace BioAccess.Web.ViewModels;

public sealed class DeviceHealthItem
{
    public string TerminalId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Ip { get; set; } = "";
    public string Status { get; set; } = "Healthy";
    public int ErrorCount { get; set; }
    public int ActivityCount { get; set; }
    public DateTime? LastSeen { get; set; }
    public string ProblemSummary { get; set; } = "";
}

public sealed class DevicesObservabilityViewModel
{
    public int TotalDevices { get; set; }
    public int HealthyDevices { get; set; }
    public int WarningDevices { get; set; }
    public int ProblemDevices { get; set; }
    public List<DeviceHealthItem> Devices { get; set; } = new();
    public string? Error { get; set; }
}
