using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Terminals.Web.Persistence;
using Terminals.Web.Services.Activity;

[ApiController]
[Route("api/regions")]
public class RegionsController : ControllerBase
{
    private readonly LocalAppDbContext _db;
    private readonly IActivityLogService _activity;

    public RegionsController(LocalAppDbContext db, IActivityLogService activity)
    {
        _db = db;
        _activity = activity;
    }

    private IActionResult? RequireAdmin()
    {
        var isAdmin = HttpContext.Session.GetString("IsAdmin") == "1";
        return isAdmin ? null : Forbid();
    }

    // GET: /api/regions
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        if (RequireAdmin() is IActionResult forbid) return forbid;

        var regions = await _db.Regions
            .AsNoTracking()
            .OrderBy(r => r.Id)
            .GroupJoin(
                _db.TerminalRegionMaps.AsNoTracking(),
                r => r.Id,
                m => m.RegionId,
                (r, maps) => new
                {
                    id = r.Id,
                    name = r.Name,
                    deviceCount = maps.Count()
                })
            .ToListAsync(ct);

        return Ok(regions);
    }

    public sealed class CreateRegionRequest
    {
        public string Name { get; set; } = "";
    }

    // POST: /api/regions
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateRegionRequest req, CancellationToken ct)
    {
        if (RequireAdmin() is IActionResult forbid) return forbid;

        var name = (req?.Name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest(new { message = "Ø§Ø³Ù… Ø§Ù„Ù…Ù†Ø·Ù‚Ø© Ù…Ø·Ù„ÙˆØ¨." });

        if (name.Length > 100)
            return BadRequest(new { message = "Ø§Ø³Ù… Ø§Ù„Ù…Ù†Ø·Ù‚Ø© Ø·ÙˆÙŠÙ„ (Ø§Ù„Ø­Ø¯ Ø§Ù„Ø£Ù‚ØµÙ‰ 100 Ø­Ø±Ù)." });

        // (Ø§Ø®ØªÙŠØ§Ø±ÙŠ) Ù…Ù†Ø¹ ØªÙƒØ±Ø§Ø± Ø§Ù„Ø£Ø³Ù…Ø§Ø¡ Ø¨Ø´ÙƒÙ„ Ø­Ø³Ø§Ø³/ØºÙŠØ± Ø­Ø³Ø§Ø³ Ù‚Ø¯ ÙŠØ¹ØªÙ…Ø¯ Ø¹Ù„Ù‰ collation.
        var exists = await _db.Regions.AsNoTracking().AnyAsync(x => x.Name == name, ct);
        if (exists)
            return Conflict(new { message = "Ù‡Ø°Ù‡ Ø§Ù„Ù…Ù†Ø·Ù‚Ø© Ù…ÙˆØ¬ÙˆØ¯Ø© Ù…Ø³Ø¨Ù‚Ù‹Ø§." });

        var region = new Terminals.Web.Persistence.Entities.Region
        {
            Name = name
        };

        _db.Regions.Add(region);
        await _db.SaveChangesAsync(ct);

        await _activity.LogAsync(
            action: "Region.Created",
            entityType: "Region",
            entityId: region.Id.ToString(),
            summary: $"ØªÙ… Ø¥Ù†Ø´Ø§Ø¡ Ù…Ù†Ø·Ù‚Ø© Ø¬Ø¯ÙŠØ¯Ø©: {region.Id} ({region.Name}).",
            details: new { regionId = region.Id, regionName = region.Name },
            ct: ct
        );

        return Ok(new { id = region.Id, name = region.Name });
    }

    public sealed class RenameRegionRequest
    {
        public string Name { get; set; } = "";
    }

    // PUT: /api/regions/{regionId}
    [HttpPut("{regionId:int}")]
    public async Task<IActionResult> Rename(int regionId, [FromBody] RenameRegionRequest req, CancellationToken ct)
    {
        if (RequireAdmin() is IActionResult forbid) return forbid;

        var name = (req?.Name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest(new { message = "Ø§Ø³Ù… Ø§Ù„Ù…Ù†Ø·Ù‚Ø© Ù…Ø·Ù„ÙˆØ¨." });

        if (name.Length > 100)
            return BadRequest(new { message = "Ø§Ø³Ù… Ø§Ù„Ù…Ù†Ø·Ù‚Ø© Ø·ÙˆÙŠÙ„ (Ø§Ù„Ø­Ø¯ Ø§Ù„Ø£Ù‚ØµÙ‰ 100 Ø­Ø±Ù)." });

        var region = await _db.Regions.FirstOrDefaultAsync(x => x.Id == regionId, ct);
        if (region is null)
            return NotFound(new { message = "Ø§Ù„Ù…Ù†Ø·Ù‚Ø© ØºÙŠØ± Ù…ÙˆØ¬ÙˆØ¯Ø©." });

        var oldName = region.Name;
        region.Name = name;
        await _db.SaveChangesAsync(ct);

        await _activity.LogAsync(
            action: "Region.Renamed",
            entityType: "Region",
            entityId: region.Id.ToString(),
            summary: $"ØªÙ… ØªØ¹Ø¯ÙŠÙ„ Ø§Ø³Ù… Ø§Ù„Ù…Ù†Ø·Ù‚Ø© {region.Id}: \"{oldName}\" â†’ \"{region.Name}\".",
            details: new { regionId = region.Id, oldName, newName = region.Name },
            ct: ct
        );

        return Ok(new { message = "ØªÙ… ØªØ­Ø¯ÙŠØ« Ø§Ø³Ù… Ø§Ù„Ù…Ù†Ø·Ù‚Ø©." });
    }

    // DELETE: /api/regions/{regionId}
    [HttpDelete("{regionId:int}")]
    public async Task<IActionResult> Delete(int regionId, CancellationToken ct)
    {
        if (RequireAdmin() is IActionResult forbid) return forbid;

        var region = await _db.Regions.FirstOrDefaultAsync(x => x.Id == regionId, ct);
        if (region is null)
            return NotFound(new { message = "Ø§Ù„Ù…Ù†Ø·Ù‚Ø© ØºÙŠØ± Ù…ÙˆØ¬ÙˆØ¯Ø©." });

        var hasDevices = await _db.TerminalRegionMaps.AsNoTracking().AnyAsync(x => x.RegionId == regionId, ct);
        if (hasDevices)
            return Conflict(new { message = "Ù„Ø§ ÙŠÙ…ÙƒÙ† Ø­Ø°Ù Ø§Ù„Ù…Ù†Ø·Ù‚Ø© Ù„Ø£Ù†Ù‡Ø§ Ù…Ø±ØªØ¨Ø·Ø© Ø¨Ø£Ø¬Ù‡Ø²Ø©." });

        var name = region.Name;
        _db.Regions.Remove(region);
        await _db.SaveChangesAsync(ct);

        await _activity.LogAsync(
            action: "Region.Deleted",
            entityType: "Region",
            entityId: regionId.ToString(),
            summary: $"ØªÙ… Ø­Ø°Ù Ø§Ù„Ù…Ù†Ø·Ù‚Ø© {regionId} ({name}).",
            details: new { regionId, regionName = name },
            ct: ct
        );

        return Ok(new { message = "ØªÙ… Ø­Ø°Ù Ø§Ù„Ù…Ù†Ø·Ù‚Ø©." });
    }

    // âœ… GET: /api/regions/{regionId}/terminals
    [HttpGet("{regionId:int}/terminals")]
    public async Task<IActionResult> GetRegionTerminals(int regionId, CancellationToken ct)
    {
        if (RequireAdmin() is IActionResult forbid) return forbid;

        var items = await _db.TerminalRegionMaps
            .Where(x => x.RegionId == regionId)
            .OrderBy(x => x.TerminalId)
            .Select(x => new
            {
                terminalId = x.TerminalId,
                regionId = x.RegionId
            })
            .ToListAsync(ct);

        return Ok(items);
    }
}
