using Terminals.Web.Contracts;
using Terminals.Web.DTOs;
using Terminals.Web.External;
using Terminals.Web.Persistence;
using Terminals.Web.Services.Terminals;
using Microsoft.EntityFrameworkCore;

namespace Terminals.Web.Services.Employees;

// Builds the employee device screen from SOAP, Alpeta, and local DB data.
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
        // Read employee info from SOAP and current terminal links from Alpeta.
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
        // === Employee screen state ===
        // This combines permanent links, active delegations, and region mapping.
        var now = DateTime.Now;

        // Active delegations act like temporary assignments on the screen.
        var activeDelegatedTerminalIds = await _db.Delegations
            .Where(d =>
                d.EmployeeId == employeeId &&
                d.Status == "Active" &&
                d.StartDate <= now &&
                d.EndDate > now)
            .SelectMany(d => d.Terminals.Select(t => t.TerminalId))
            .ToHashSetAsync(ct);

        // Load scheduled and active delegation rows so the UI can show status and dates.
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

        // Keep one delegation row per terminal. Active rows win over scheduled rows.
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

        // Use a set because the screen checks assignment state for each terminal.
        var assignedSet = new HashSet<string>(
            assigned.Where(x => !string.IsNullOrWhiteSpace(x.DeviceId)).Select(x => x.DeviceId!.Trim()),
            StringComparer.OrdinalIgnoreCase);

        // Region mapping is local business data. Alpeta only gives the terminals.
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

            // Build one row with all states the page needs for actions and badges.
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
                RegionName = string.IsNullOrWhiteSpace(regName) ? "Ø£Ø¬Ù‡Ø²Ø© ØºÙŠØ± Ù…ØµÙ†ÙØ©" : regName
            });
        }

        // Assigned devices whose IDs are absent from allDevices would be silently dropped by the
        // loop above. A second pass ensures they always appear, using the placeholder DeviceDto
        // that GetEmployeeDevicesAsync already provides for exactly this case.
        var coveredIds = new HashSet<string>(
            rows.Where(r => !string.IsNullOrWhiteSpace(r.DeviceId)).Select(r => r.DeviceId!),
            StringComparer.OrdinalIgnoreCase);

        foreach (var d in assigned)
        {
            var id = d.DeviceId?.Trim();
            if (string.IsNullOrWhiteSpace(id) || coveredIds.Contains(id)) continue;

            mappings.TryGetValue(id, out var regId);
            regionNameById.TryGetValue(regId, out var regName);
            var isDelegatedActive = activeDelegatedTerminalIds.Contains(id);
            delegatedRowsByTerminal.TryGetValue(id, out var delRow);

            rows.Add(new DeviceRowDto
            {
                DeviceId = id,
                DeviceName = d.DeviceName,
                IsAssigned = true,
                IsDelegated = delRow != null,
                IsDelegatedActive = isDelegatedActive,
                DelegationId = delRow?.DelegationId,
                DelegationStatus = delRow?.Status,
                IsEffectivelyAssigned = true,
                DelegationStartDate = delRow?.StartDate,
                DelegationEndDate = delRow?.EndDate,
                RegionId = regId == 0 ? null : regId,
                RegionName = string.IsNullOrWhiteSpace(regName) ? "Ø£Ø¬Ù‡Ø²Ø© ØºÙŠØ± Ù…ØµÙ†ÙØ©" : regName
            });
        }

        // Group devices by region so the page can show them as region sections.
        var groups = rows
            .GroupBy(x => new { x.RegionId, RegionName = string.IsNullOrWhiteSpace(x.RegionName) ? "Ø£Ø¬Ù‡Ø²Ø© ØºÙŠØ± Ù…ØµÙ†ÙØ©" : x.RegionName })
            .OrderByDescending(g => g.Any(x => x.IsEffectivelyAssigned))
            .ThenBy(g => g.Key.RegionName == "Ø£Ø¬Ù‡Ø²Ø© ØºÙŠØ± Ù…ØµÙ†ÙØ©" ? 1 : 0)
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
        // Permanent assignment goes straight to Alpeta.
       => _alpeta.AssignUserToTerminalAsync(terminalId, employeeId, ct);

    public Task<bool> UnassignOneAsync(int employeeId, string terminalId, CancellationToken ct = default)
        // Permanent unassignment goes straight to Alpeta.
        => _alpeta.UnassignUserFromTerminalAsync(terminalId, employeeId, ct);
}
