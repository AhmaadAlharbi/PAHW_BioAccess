namespace Terminals.Web.ViewModels;

public class RegionGroupVm
{
    public int? RegionId { get; set; }
    public string RegionName { get; set; } = "ØºÙŠØ± Ù…ØµÙ†Ù";

    public int TotalDevices { get; set; }
    public int AssignedDevices { get; set; }

    public List<DeviceRowVm> Devices { get; set; } = new();
}
