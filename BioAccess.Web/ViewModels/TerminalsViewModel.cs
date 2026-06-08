namespace BioAccess.Web.ViewModels;

public sealed class TerminalsViewModel
{
    public List<TerminalRegionViewModel> Regions { get; set; } = new();
    public List<TerminalDeviceViewModel> Devices { get; set; } = new();
    public int StaleMappingsCount { get; set; }
    // True when the device list came from the local fallback cache rather than a live Alpeta response.
    public bool ApiUnavailable { get; set; }
}

public sealed class TerminalRegionViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int DeviceCount { get; set; }
}

public sealed class TerminalDeviceViewModel
{
    public string DeviceId { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public string Location { get; set; } = "";
    public int? RegionId { get; set; }
    public string? RegionName { get; set; }
    public string Status { get; set; } = "Unassigned";
}
