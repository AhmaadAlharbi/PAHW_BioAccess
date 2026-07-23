using Terminals.Web.External;
using Terminals.Web.DTOs;
using Terminals.Web.Persistence;
using Terminals.Web.Persistence.Entities;
using Terminals.Web.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace Terminals.Web.Services.Terminals;

public sealed class TerminalService
{
    private readonly LocalAppDbContext _db;
    private readonly AlpetaClient _alpeta;

    public TerminalService(LocalAppDbContext db, AlpetaClient alpeta)
    {
        _db = db;
        _alpeta = alpeta;
    }

    public async Task<List<TerminalRegionViewModel>> GetRegionsAsync(CancellationToken ct = default)
    {
        return await _db.Regions
            .AsNoTracking()
            .OrderBy(r => r.Id)
            .GroupJoin(
                _db.TerminalRegionMaps.AsNoTracking(),
                r => r.Id,
                m => m.RegionId,
                (r, maps) => new TerminalRegionViewModel
                {
                    Id = r.Id,
                    Name = r.Name,
                    DeviceCount = maps.Count()
                })
            .ToListAsync(ct);
    }

    public async Task<TerminalsViewModel> GetTerminalsViewModelAsync(CancellationToken ct = default)
    {
        var devices = await _alpeta.GetAllDevicesAsync(ct);

        return new TerminalsViewModel
        {
            Regions          = await GetRegionsAsync(ct),
            Devices          = await BuildTerminalsAsync(devices, ct),
            StaleMappingsCount = await GetStaleMappingsCountAsync(devices, ct),
            ApiUnavailable   = _alpeta.LastCallUsedFallback
        };
    }

    public async Task<List<TerminalDeviceViewModel>> GetTerminalsAsync(CancellationToken ct = default)
    {
        var devices = await _alpeta.GetAllDevicesAsync(ct);
        return await BuildTerminalsAsync(devices, ct);
    }

    private async Task<List<TerminalDeviceViewModel>> BuildTerminalsAsync(
        IReadOnlyCollection<DeviceDto> devices,
        CancellationToken ct = default)
    {
        var regionNameById = await _db.Regions
            .AsNoTracking()
            .ToDictionaryAsync(x => x.Id, x => x.Name, ct);

        var maps = await _db.TerminalRegionMaps
            .AsNoTracking()
            .ToListAsync(ct);

        var mapByTerminalId = maps.ToDictionary(x => x.TerminalId, x => x.RegionId, StringComparer.OrdinalIgnoreCase);

        return devices
            .Where(d => !string.IsNullOrWhiteSpace(d.DeviceId))
            .Select(d =>
            {
                var id = d.DeviceId.Trim();
                mapByTerminalId.TryGetValue(id, out var regionId);
                regionNameById.TryGetValue(regionId, out var regionName);

                return new TerminalDeviceViewModel
                {
                    DeviceId = id,
                    DeviceName = d.DeviceName,
                    Location = d.Location,
                    RegionId = regionId == 0 ? null : regionId,
                    RegionName = string.IsNullOrWhiteSpace(regionName) ? null : regionName,
                    Status = regionId == 0 ? "Unassigned" : "Assigned"
                };
            })
            .OrderBy(x => x.Status == "Unassigned" ? 0 : 1)
            .ThenBy(x => x.RegionName ?? "")
            .ThenBy(x => x.DeviceName ?? "")
            .ThenBy(x => x.DeviceId)
            .ToList();
    }

    public async Task AssignRegionAsync(string terminalId, int regionId, CancellationToken ct = default)
    {
        terminalId = (terminalId ?? "").Trim();
        if (string.IsNullOrWhiteSpace(terminalId))
            throw new InvalidOperationException("TerminalId Ù…Ø·Ù„ÙˆØ¨.");

        if (regionId <= 0)
            throw new InvalidOperationException("RegionId ØºÙŠØ± ØµØ­ÙŠØ­.");

        var regionExists = await _db.Regions.AsNoTracking().AnyAsync(x => x.Id == regionId, ct);
        if (!regionExists)
            throw new InvalidOperationException("Ø§Ù„Ù…Ù†Ø·Ù‚Ø© ØºÙŠØ± Ù…ÙˆØ¬ÙˆØ¯Ø©.");

        var row = await _db.TerminalRegionMaps.FirstOrDefaultAsync(x => x.TerminalId == terminalId, ct);
        if (row is null)
            _db.TerminalRegionMaps.Add(new TerminalRegionMap { TerminalId = terminalId, RegionId = regionId });
        else
            row.RegionId = regionId;

        await _db.SaveChangesAsync(ct);
    }

    public async Task CreateRegionAsync(string name, CancellationToken ct = default)
    {
        name = (name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Ø§Ø³Ù… Ø§Ù„Ù…Ù†Ø·Ù‚Ø© Ù…Ø·Ù„ÙˆØ¨.");

        if (name.Length > 100)
            throw new InvalidOperationException("Ø§Ø³Ù… Ø§Ù„Ù…Ù†Ø·Ù‚Ø© Ø·ÙˆÙŠÙ„.");

        var exists = await _db.Regions.AsNoTracking().AnyAsync(x => x.Name == name, ct);
        if (exists)
            throw new InvalidOperationException("Ù‡Ø°Ù‡ Ø§Ù„Ù…Ù†Ø·Ù‚Ø© Ù…ÙˆØ¬ÙˆØ¯Ø© Ù…Ø³Ø¨Ù‚Ø§Ù‹.");

        _db.Regions.Add(new Region { Name = name });
        await _db.SaveChangesAsync(ct);
    }

    public async Task RenameRegionAsync(int regionId, string name, CancellationToken ct = default)
    {
        name = (name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Ø§Ø³Ù… Ø§Ù„Ù…Ù†Ø·Ù‚Ø© Ù…Ø·Ù„ÙˆØ¨.");

        if (name.Length > 100)
            throw new InvalidOperationException("Ø§Ø³Ù… Ø§Ù„Ù…Ù†Ø·Ù‚Ø© Ø·ÙˆÙŠÙ„.");

        var region = await _db.Regions.FirstOrDefaultAsync(x => x.Id == regionId, ct);
        if (region is null)
            throw new InvalidOperationException("Ø§Ù„Ù…Ù†Ø·Ù‚Ø© ØºÙŠØ± Ù…ÙˆØ¬ÙˆØ¯Ø©.");

        region.Name = name;
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteRegionAsync(int regionId, CancellationToken ct = default)
    {
        var region = await _db.Regions.FirstOrDefaultAsync(x => x.Id == regionId, ct);
        if (region is null)
            throw new InvalidOperationException("Ø§Ù„Ù…Ù†Ø·Ù‚Ø© ØºÙŠØ± Ù…ÙˆØ¬ÙˆØ¯Ø©.");

        var hasDevices = await _db.TerminalRegionMaps.AsNoTracking().AnyAsync(x => x.RegionId == regionId, ct);
        if (hasDevices)
            throw new InvalidOperationException("Ù„Ø§ ÙŠÙ…ÙƒÙ† Ø­Ø°Ù Ø§Ù„Ù…Ù†Ø·Ù‚Ø© Ù„Ø£Ù†Ù‡Ø§ Ù…Ø±ØªØ¨Ø·Ø© Ø¨Ø£Ø¬Ù‡Ø²Ø©.");

        _db.Regions.Remove(region);
        await _db.SaveChangesAsync(ct);
    }

    public async Task ClearRegionAsync(string terminalId, CancellationToken ct = default)
    {
        terminalId = (terminalId ?? "").Trim();
        if (string.IsNullOrWhiteSpace(terminalId))
            throw new InvalidOperationException("TerminalId Ù…Ø·Ù„ÙˆØ¨.");

        var row = await _db.TerminalRegionMaps.FirstOrDefaultAsync(x => x.TerminalId == terminalId, ct);
        if (row is null)
            return;

        _db.TerminalRegionMaps.Remove(row);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<int> GetStaleMappingsCountAsync(CancellationToken ct = default)
    {
        var devices = await _alpeta.GetAllDevicesAsync(ct);
        return await GetStaleMappingsCountAsync(devices, ct);
    }

    private async Task<int> GetStaleMappingsCountAsync(
        IReadOnlyCollection<DeviceDto> devices,
        CancellationToken ct = default)
    {
        var deviceIds = new HashSet<string>(
            devices.Select(x => x.DeviceId?.Trim() ?? ""),
            StringComparer.OrdinalIgnoreCase);

        return await _db.TerminalRegionMaps
            .AsNoTracking()
            .CountAsync(x => !deviceIds.Contains(x.TerminalId ?? ""), ct);
    }
}
