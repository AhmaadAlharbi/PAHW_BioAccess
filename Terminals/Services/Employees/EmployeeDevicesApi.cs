using Terminals.Web.Contracts;
using Terminals.Web.DTOs;
using Terminals.Web.External;
using Terminals.Web.Persistence;
using Terminals.Web.Services.Observability;
using Terminals.Web.Services.Restrictions;
using Terminals.Web.Services.Terminals;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Terminals.Web.Services.Employees;

// Builds the employee device screen from SOAP, Alpeta, and local DB data.
public class EmployeeDevicesApi : IEmployeeDevicesApi
{
    private readonly EmployeeSoapClient _soap;
    private readonly AlpetaClient _alpeta;
    private readonly RegionMappingService _regions;
    private readonly LocalAppDbContext _db;
    private readonly DeviceRestrictionService _restrictionService;
    private readonly DeviceObservabilityService _observability;
    private readonly ILogger<EmployeeDevicesApi> _logger;

    public EmployeeDevicesApi(
        EmployeeSoapClient soap,
        AlpetaClient alpeta,
        RegionMappingService regions,
        LocalAppDbContext db,
        DeviceRestrictionService restrictionService,
        DeviceObservabilityService observability,
        ILogger<EmployeeDevicesApi> logger)
    {
        _soap = soap;
        _alpeta = alpeta;
        _regions = regions;
        _db = db;
        _restrictionService = restrictionService;
        _observability = observability;
        _logger = logger;
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

        try
        {
            var context = await BuildAlpetaRequestContextAsync(employeeId, ct);
            await ApplyRestrictionAnalysisAsync(employeeId, context, ct);

            return new EmployeeDevicesDto
            {
                Employee = employee,
                AllDevices = context.AllDevices,
                AssignedDevices = context.EmployeeDevices
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ALPETA_READ_FAILED_UI employeeId={EmployeeId}", employeeId);
            return new EmployeeDevicesDto
            {
                Employee = employee,
                Error = BuildUserFacingReadError(ex)
            };
        }
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
        if (!string.IsNullOrWhiteSpace(baseDto.Error))
        {
            return new EmployeeDevicesScreenDto
            {
                Employee = baseDto.Employee,
                Error = baseDto.Error
            };
        }

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
        var observabilityById = await LoadObservabilityByDeviceIdAsync(ct);
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
            observabilityById.TryGetValue(id, out var health);

            rows.Add(new DeviceRowDto
            {
                DeviceId = id,
                DeviceName = d.DeviceName,
                Status = health?.Status ?? "Active",
                LastSeen = health?.LastSeen,
                IsRestricted = d.IsRestricted,
                RestrictionReason = d.RestrictionReason,
                RestrictionSource = d.RestrictionSource,
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
            observabilityById.TryGetValue(id, out var health);

            rows.Add(new DeviceRowDto
            {
                DeviceId = id,
                DeviceName = d.DeviceName,
                Status = health?.Status ?? "Active",
                LastSeen = health?.LastSeen,
                IsRestricted = d.IsRestricted,
                RestrictionReason = d.RestrictionReason,
                RestrictionSource = d.RestrictionSource,
                IsAssigned = true,
                IsDelegated = delRow != null,
                IsDelegatedActive = isDelegatedActive,
                DelegationId = delRow?.DelegationId,
                DelegationStatus = delRow?.Status,
                IsEffectivelyAssigned = true,
                DelegationStartDate = delRow?.StartDate,
                DelegationEndDate = delRow?.EndDate,
                RegionId = regId == 0 ? null : regId,
                RegionName = string.IsNullOrWhiteSpace(regName) ? "أجهزة غير مصنفة" : regName
            });
        }

        // Group devices by region so the page can show them as region sections.
        var groups = rows
            .GroupBy(x => new { x.RegionId, RegionName = string.IsNullOrWhiteSpace(x.RegionName) ? "أجهزة غير مصنفة" : x.RegionName })
            .OrderByDescending(g => g.Any(x => x.IsEffectivelyAssigned))
            .ThenBy(g => g.Key.RegionName == "أجهزة غير مصنفة" ? 1 : 0)
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

    private async Task<Dictionary<string, ViewModels.DeviceHealthItem>> LoadObservabilityByDeviceIdAsync(CancellationToken ct)
    {
        try
        {
            var model = await _observability.GetDevicesAsync(ct);
            return model.Devices
                .Where(x => !string.IsNullOrWhiteSpace(x.TerminalId))
                .GroupBy(x => x.TerminalId.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "EMPLOYEE_DEVICE_OBSERVABILITY_LOAD_FAILED");
            return new Dictionary<string, ViewModels.DeviceHealthItem>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public Task<OperationResult> AssignOneAsync(int employeeId, string terminalId, CancellationToken ct = default)
        // Permanent assignment goes straight to Alpeta.
       => _alpeta.AssignUserToTerminalAsync(terminalId, employeeId, ct);

    public async Task<OperationResult> UnassignOneAsync(int employeeId, string terminalId, CancellationToken ct = default)
    {
        var result = await _alpeta.UnassignUserFromTerminalAsync(terminalId, employeeId, ct);
        if (!result.Success) return result;

        // If an active delegation covers this terminal, mark it so the worker won't re-assign it.
        var trimmedTerminalId = terminalId.Trim();
        var rows = await _db.DelegationTerminals
            .Include(t => t.Delegation)
            .Where(t => t.TerminalId == trimmedTerminalId &&
                        t.Delegation != null &&
                        t.Delegation.EmployeeId == employeeId &&
                        t.Delegation.Status == "Active")
            .ToListAsync(ct);

        if (rows.Count > 0)
        {
            foreach (var row in rows)
                row.IsManuallyRemoved = true;
            await _db.SaveChangesAsync(ct);
        }

        return result;
    }

    private static string BuildUserFacingReadError(Exception ex)
    {
        var message = ex.Message ?? "";
        if (message.Contains("session", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("timeout", StringComparison.OrdinalIgnoreCase))
        {
            return "فشل الاتصال بالنظام الخارجي، يرجى إعادة المحاولة";
        }

        return "تعذر تحميل الأجهزة حالياً، يرجى المحاولة لاحقاً";
    }

    private async Task ApplyRestrictionAnalysisAsync(
        int employeeId,
        AlpetaRequestContext context,
        CancellationToken ct)
    {
        _restrictionService.PrimeEmployeeAssignments(
            employeeId,
            context.EmployeeDevices
                .Where(x => !string.IsNullOrWhiteSpace(x.DeviceId))
                .Select(x => x.DeviceId!));

        var devicesToAnalyze = context.AllDevices
            .Concat(context.EmployeeDevices)
            .Where(x => !string.IsNullOrWhiteSpace(x.DeviceId))
            .GroupBy(x => x.DeviceId!.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        foreach (var device in devicesToAnalyze)
        {
            if (!int.TryParse(device.DeviceId.Trim(), out var terminalId))
                continue;

            try
            {
                var analysis = await _restrictionService.AnalyzeAsync(employeeId, terminalId, context, ct);
                device.IsRestricted = analysis.IsRestricted;
                device.RestrictionReason = string.IsNullOrWhiteSpace(analysis.Reason) ? null : analysis.Reason;
                device.RestrictionSource = string.IsNullOrWhiteSpace(analysis.Source) ? null : analysis.Source;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "DEVICE_RESTRICTION_ANALYSIS_FAILED employeeId={EmployeeId} terminalId={TerminalId}",
                    employeeId,
                    terminalId);
            }
        }

        var analysisById = devicesToAnalyze
            .Where(x => !string.IsNullOrWhiteSpace(x.DeviceId))
            .ToDictionary(x => x.DeviceId.Trim(), x => x, StringComparer.OrdinalIgnoreCase);

        foreach (var device in context.AllDevices.Concat(context.EmployeeDevices))
        {
            var deviceId = device.DeviceId?.Trim();
            if (string.IsNullOrWhiteSpace(deviceId) || !analysisById.TryGetValue(deviceId, out var analyzed))
                continue;

            device.IsRestricted = analyzed.IsRestricted;
            device.RestrictionReason = analyzed.RestrictionReason;
            device.RestrictionSource = analyzed.RestrictionSource;
        }
    }

    private async Task<AlpetaRequestContext> BuildAlpetaRequestContextAsync(int employeeId, CancellationToken ct)
    {
        var allDevicesSnapshot = await _alpeta.GetAllDevicesSnapshotAsync(ct);
        var allDevices = allDevicesSnapshot.Devices;

        var employeeDevicesTask = _alpeta.GetEmployeeDevicesAsync(employeeId, allDevices, ct);
        var accessGroupsTask = _alpeta.GetAccessGroupsDocumentAsync(ct);
        var groupsTask = _alpeta.GetGroupsDocumentAsync(ct);
        var privilegesTask = _alpeta.GetPrivilegesDocumentAsync(ct);

        await Task.WhenAll(employeeDevicesTask, accessGroupsTask, groupsTask, privilegesTask);

        return new AlpetaRequestContext
        {
            AllDevices = allDevices,
            EmployeeDevices = await employeeDevicesTask,
            AccessGroups = await accessGroupsTask,
            Groups = await groupsTask,
            Privileges = await privilegesTask,
            Terminals = allDevicesSnapshot.TerminalsDocument
        };
    }
}
