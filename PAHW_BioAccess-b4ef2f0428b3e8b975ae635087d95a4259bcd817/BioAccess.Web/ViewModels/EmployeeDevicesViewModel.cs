namespace Terminals.Web.ViewModels;

public class EmployeeDevicesViewModel
{
    public Terminals.Web.DTOs.EmployeeDto? Employee { get; set; }

    public List<DeviceRowVm> Devices { get; set; } = new();

    // âœ… Ø§Ù„Ø¬Ø¯ÙŠØ¯: ØªØ¬Ù…ÙŠØ¹ Ø­Ø³Ø¨ Ø§Ù„Ù…Ù†Ø§Ø·Ù‚ Ù„Ù„Ø¹Ø±Ø¶ Ø¨Ø´ÙƒÙ„ Accordion
    public List<RegionGroupVm> RegionGroups { get; set; } = new();

    public string? ErrorMessage { get; set; }
}
