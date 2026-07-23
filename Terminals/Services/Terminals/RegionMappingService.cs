using Terminals.Web.Persistence.Entities;
using Terminals.Web.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Terminals.Web.Services.Terminals;

public class RegionMappingService
{
    private readonly LocalAppDbContext _db;
    public RegionMappingService(LocalAppDbContext db) => _db = db;

    public Task<List<Region>> GetRegionsAsync(CancellationToken ct = default)
        => _db.Regions.OrderBy(x => x.Id).ToListAsync(ct);

    public async Task<int?> GetRegionIdForTerminalAsync(string terminalId, CancellationToken ct = default)
        => await _db.TerminalRegionMaps
            .Where(x => x.TerminalId == terminalId)
            .Select(x => (int?)x.RegionId)
            .FirstOrDefaultAsync(ct);

    public async Task UpsertTerminalRegionAsync(string terminalId, int regionId, CancellationToken ct = default)
    {
        var row = await _db.TerminalRegionMaps.FirstOrDefaultAsync(x => x.TerminalId == terminalId, ct);

        if (row is null)
            _db.TerminalRegionMaps.Add(new TerminalRegionMap { TerminalId = terminalId, RegionId = regionId });
        else
            row.RegionId = regionId;

        await _db.SaveChangesAsync(ct);
    }

    public async Task<Dictionary<string, int>> GetAllMappingsAsync(CancellationToken ct = default)
        => await _db.TerminalRegionMaps.ToDictionaryAsync(x => x.TerminalId, x => x.RegionId, ct);
}
