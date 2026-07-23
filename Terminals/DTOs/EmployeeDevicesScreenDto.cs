namespace Terminals.Web.DTOs;

public sealed class EmployeeDevicesScreenDto
{
    public EmployeeDto Employee { get; set; } = default!;
    public string? Error { get; set; }
    public List<DeviceRowDto> Devices { get; set; } = new();
    public List<RegionGroupDto> RegionGroups { get; set; } = new();
}

public class DeviceRowDto
{
    public string DeviceId { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public string Location { get; set; } = "";
    public string Status { get; set; } = "Active";
    public DateTime? LastSeen { get; set; }
    public bool IsRestricted { get; set; }
    public string? RestrictionReason { get; set; }
    public string? RestrictionSource { get; set; }
    public bool IsAssigned { get; set; }

    // ✅ جديد
    public bool IsDelegated { get; set; }
    public bool IsDelegatedActive { get; set; }
    public int? DelegationId { get; set; }
    public string? DelegationStatus { get; set; } // "Scheduled" | "Active" | ...
    public bool IsEffectivelyAssigned { get; set; }
    public DateTime? DelegationStartDate { get; set; }
    public DateTime? DelegationEndDate { get; set; }

    public int? RegionId { get; set; }
    public string? RegionName { get; set; }
}


public sealed class RegionGroupDto
{
    public int? RegionId { get; set; }
    public string RegionName { get; set; } = "";
    public int TotalDevices { get; set; }
    public int AssignedDevices { get; set; }
    public List<DeviceRowDto> Devices { get; set; } = new();
}
