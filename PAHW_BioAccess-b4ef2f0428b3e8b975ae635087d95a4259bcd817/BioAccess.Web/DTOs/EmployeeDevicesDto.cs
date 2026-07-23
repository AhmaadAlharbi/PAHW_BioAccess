namespace Terminals.Web.DTOs
{
    public class EmployeeDevicesDto
    {
        public EmployeeDto Employee { get; set; } = new();

        // ÙƒÙ„ Ø§Ù„Ø£Ø¬Ù‡Ø²Ø© (Ù…Ù† Alpeta)
        public List<DeviceDto> AllDevices { get; set; } = new();

        // Ø£Ø¬Ù‡Ø²Ø© Ø§Ù„Ù…ÙˆØ¸Ù Ø§Ù„Ù…Ø±ØªØ¨Ø·Ø© (Ù…Ù† Alpeta)
        public List<DeviceDto> AssignedDevices { get; set; } = new();
    }
}
