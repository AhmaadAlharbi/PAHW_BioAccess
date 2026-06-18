namespace BioAccess.Web.ViewModels;

public class RegionGroupVm
{
    public int? RegionId { get; set; }
    public string RegionName { get; set; } = "غير مصنف";

    public int TotalDevices { get; set; }
    public int AssignedDevices { get; set; }

    public List<DeviceRowVm> Devices { get; set; } = new();
}
