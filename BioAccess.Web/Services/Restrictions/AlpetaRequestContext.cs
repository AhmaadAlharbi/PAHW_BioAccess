using System.Text.Json;
using BioAccess.Web.DTOs;

namespace BioAccess.Web.Services.Restrictions;

public sealed class AlpetaRequestContext
{
    public List<DeviceDto> AllDevices { get; init; } = new();
    public List<DeviceDto> EmployeeDevices { get; init; } = new();
    public JsonDocument? AccessGroups { get; init; }
    public JsonDocument? Groups { get; init; }
    public JsonDocument? Privileges { get; init; }
    public JsonDocument? Terminals { get; init; }
}
