using BioAccess.Web.DTOs;

namespace BioAccess.Web.ViewModels;

public class DeviceRowVm : DeviceDto
{
    public bool IsAssigned { get; set; }
    public int? RegionId { get; set; }
    public string? RegionName { get; set; }
}
