namespace BioAccess.Web.DTOs
{
    public class EmployeeDevicesDto
    {
        public EmployeeDto Employee { get; set; } = new();

        // كل الأجهزة (من Alpeta)
        public List<DeviceDto> AllDevices { get; set; } = new();

        // أجهزة الموظف المرتبطة (من Alpeta)
        public List<DeviceDto> AssignedDevices { get; set; } = new();
    }
}
