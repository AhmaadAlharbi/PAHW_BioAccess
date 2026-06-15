namespace BioAccess.Web.DTOs
{
    public class EmployeeDevicesDto
    {
        public EmployeeDto Employee { get; set; } = new();
        public string? Error { get; set; }

        // كل الأجهزة (من Alpeta)
        public List<DeviceDto> AllDevices { get; set; } = new();

        // أجهزة الموظف المرتبطة (من Alpeta)
        public List<DeviceDto> AssignedDevices { get; set; } = new();
    }
}
