namespace BioAccess.Web.ViewModels;

public sealed class TerminalsViewModel
{
    public List<TerminalRegionViewModel> Regions { get; set; } = new();
    public List<TerminalDeviceViewModel> Devices { get; set; } = new();
    public int StaleMappingsCount { get; set; }
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
