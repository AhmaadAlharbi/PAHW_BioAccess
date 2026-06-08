using BioAccess.Web.Contracts;
using BioAccess.Web.DTOs;
using BioAccess.Web.External;
using BioAccess.Web.Persistence;
using BioAccess.Web.Services.Terminals;
using Microsoft.EntityFrameworkCore;

namespace BioAccess.Web.Services.Employees;

public class EmployeeDevicesApi : IEmployeeDevicesApi
{
    private readonly EmployeeSoapClient _soap;
    private readonly AlpetaClient _alpeta;
    private readonly RegionMappingService _regions;
    private readonly LocalAppDbContext _db;

    public EmployeeDevicesApi(
        EmployeeSoapClient soap,
        AlpetaClient alpeta,
        RegionMappingService regions,
        LocalAppDbContext db)
    {
        _soap = soap;
        _alpeta = alpeta;
        _regions = regions;
        _db = db;
    }

    public async Task<EmployeeDevicesDto?> GetEmployeeWithDevicesAsync(int employeeId, CancellationToken ct = default)
    {
        if (employeeId <= 0) return null;

        var raw = await _soap.GetEmployeeByIdRawAsync(employeeId, ct);
        var (name, dept, title) = _soap.ParseEmployeeSummary(raw);
        if (string.IsNullOrWhiteSpace(name)) return null;

        var employee = new EmployeeDto
        {
            EmployeeId = employeeId,
            FullNameAr = name,
            Department = dept,
            JobTitle = title
        };

        var allDevices = await _alpeta.GetAllDevicesAsync(ct);
        var assignedDevices = await _alpeta.GetEmployeeDevicesAsync(employeeId, allDevices, ct);

        return new EmployeeDevicesDto
        {
            Employee = employee,
            AllDevices = allDevices,
            AssignedDevices = assignedDevices
        };
    }

    public async Task<EmployeeDevicesScreenDto?> GetEmployeeDevicesScreenAsync(int employeeId, CancellationToken ct = default)
    {
        var now = DateTime.Now;

        var activeDelegatedTerminalIds = await _db.Delegations
            .Where(d =>
                d.EmployeeId == employeeId &&
                d.Status == "Active" &&
                d.StartDate <= now &&
                d.EndDate > now)
            .SelectMany(d => d.Terminals.Select(t => t.TerminalId))
            .ToHashSetAsync(ct);

        var delegatedRowsList = await _db.Delegations
            .Where(d => d.EmployeeId == employeeId &&
                        (d.Status == "Active" || d.Status == "Scheduled"))
            .SelectMany(d => d.Terminals.Select(t => new
            {
                TerminalId = t.TerminalId,
                DelegationId = d.Id,
                d.Status,
                d.StartDate,
                d.EndDate
            }))
            .ToListAsync(ct);

        var delegatedRowsByTerminal = delegatedRowsList
            .Where(x => !string.IsNullOrWhiteSpace(x.TerminalId))
            .Select(x => new
            {
                TerminalId = x.TerminalId.Trim(),
                x.DelegationId,
                x.Status,
                x.StartDate,
                x.EndDate
            })
            .GroupBy(x => x.TerminalId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var active = g.FirstOrDefault(x => x.Status == "Active");
                    if (active != null) return active;
                    return g.OrderBy(x => x.StartDate).First();
                },
                StringComparer.OrdinalIgnoreCase);

        var baseDto = await GetEmployeeWithDevicesAsync(employeeId, ct);
        if (baseDto?.Employee is null) return null;

        var all = baseDto.AllDevices ?? new();
        var assigned = baseDto.AssignedDevices ?? new();

        var assignedSet = new HashSet<string>(
            assigned.Where(x => !string.IsNullOrWhiteSpace(x.DeviceId)).Select(x => x.DeviceId!.Trim()),
            StringComparer.OrdinalIgnoreCase);

        var regions = await _regions.GetRegionsAsync(ct);
        var regionNameById = regions.ToDictionary(
            x => x.Id,
            x => string.IsNullOrWhiteSpace(x.Name) ? $"Region {x.Id}" : x.Name
        );

        var mappings = await _regions.GetAllMappingsAsync(ct);
        var rows = new List<DeviceRowDto>(all.Count);

        foreach (var d in all)
        {
            var id = d.DeviceId?.Trim();
            if (string.IsNullOrWhiteSpace(id)) continue;

            mappings.TryGetValue(id, out var regId);
            regionNameById.TryGetValue(regId, out var regName);
            var isAssigned = assignedSet.Contains(id);
            var isDelegatedActive = activeDelegatedTerminalIds.Contains(id);
            delegatedRowsByTerminal.TryGetValue(id, out var delRow);
            var isDelegated = delRow != null;

            rows.Add(new DeviceRowDto
            {
                DeviceId = id,
                DeviceName = d.DeviceName,
                IsAssigned = isAssigned,
                IsDelegated = isDelegated,
                IsDelegatedActive = isDelegatedActive,
                DelegationId = delRow?.DelegationId,
                DelegationStatus = delRow?.Status,
                IsEffectivelyAssigned = isAssigned || isDelegatedActive,
                DelegationStartDate = delRow?.StartDate,
                DelegationEndDate = delRow?.EndDate,
                RegionId = regId == 0 ? null : regId,
                RegionName = string.IsNullOrWhiteSpace(regName) ? "أجهزة غير مصنفة" : regName
            });
        }

        var groups = rows
            .GroupBy(x => new { x.RegionId, RegionName = string.IsNullOrWhiteSpace(x.RegionName) ? "أجهزة غير مصنفة" : x.RegionName })
            .OrderBy(g => g.Key.RegionName == "أجهزة غير مصنفة" ? 1 : 0)
            .ThenByDescending(g => g.Any(x => x.IsAssigned))
            .ThenBy(g => g.Key.RegionName)
            .Select(g => new RegionGroupDto
            {
                RegionId = g.Key.RegionId,
                RegionName = g.Key.RegionName!,
                TotalDevices = g.Count(),
                AssignedDevices = g.Count(x => x.IsEffectivelyAssigned),
                Devices = g.OrderByDescending(x => x.IsEffectivelyAssigned)
                    .ThenByDescending(x => x.IsAssigned)
                    .ThenBy(x => x.DeviceName)
                    .ThenBy(x => x.DeviceId)
                    .ToList()
            })
            .ToList();

        return new EmployeeDevicesScreenDto
        {
            Employee = baseDto.Employee,
            Devices = rows,
            RegionGroups = groups
        };
    }

    public Task<bool> AssignOneAsync(int employeeId, string terminalId, CancellationToken ct = default)
       => _alpeta.AssignUserToTerminalAsync(terminalId, employeeId, ct);

    public Task<bool> UnassignOneAsync(int employeeId, string terminalId, CancellationToken ct = default)
        => _alpeta.UnassignUserFromTerminalAsync(terminalId, employeeId, ct);
}
