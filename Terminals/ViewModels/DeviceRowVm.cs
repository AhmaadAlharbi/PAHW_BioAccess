using Terminals.Web.DTOs;

namespace Terminals.Web.ViewModels;

public class DeviceRowVm : DeviceDto
{
    public bool IsAssigned { get; set; }
    public int? RegionId { get; set; }
    public string? RegionName { get; set; }
}
