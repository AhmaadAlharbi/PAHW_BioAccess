using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BioAccess.Web.Persistence;
using BioAccess.Web.Services.Activity;

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
            return BadRequest(new { message = "اسم المنطقة مطلوب." });

        if (name.Length > 100)
            return BadRequest(new { message = "اسم المنطقة طويل (الحد الأقصى 100 حرف)." });

        // (اختياري) منع تكرار الأسماء بشكل حساس/غير حساس قد يعتمد على collation.
        var exists = await _db.Regions.AsNoTracking().AnyAsync(x => x.Name == name, ct);
        if (exists)
            return Conflict(new { message = "هذه المنطقة موجودة مسبقًا." });

        var region = new BioAccess.Web.Persistence.Entities.Region
        {
            Name = name
        };

        _db.Regions.Add(region);
        await _db.SaveChangesAsync(ct);

        await _activity.LogAsync(
            action: "Region.Created",
            entityType: "Region",
            entityId: region.Id.ToString(),
            summary: $"تم إنشاء منطقة جديدة: {region.Id} ({region.Name}).",
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
            return BadRequest(new { message = "اسم المنطقة مطلوب." });

        if (name.Length > 100)
            return BadRequest(new { message = "اسم المنطقة طويل (الحد الأقصى 100 حرف)." });

        var region = await _db.Regions.FirstOrDefaultAsync(x => x.Id == regionId, ct);
        if (region is null)
            return NotFound(new { message = "المنطقة غير موجودة." });

        var oldName = region.Name;
        region.Name = name;
        await _db.SaveChangesAsync(ct);

        await _activity.LogAsync(
            action: "Region.Renamed",
            entityType: "Region",
            entityId: region.Id.ToString(),
            summary: $"تم تعديل اسم المنطقة {region.Id}: \"{oldName}\" → \"{region.Name}\".",
            details: new { regionId = region.Id, oldName, newName = region.Name },
            ct: ct
        );

        return Ok(new { message = "تم تحديث اسم المنطقة." });
    }

    // DELETE: /api/regions/{regionId}
    [HttpDelete("{regionId:int}")]
    public async Task<IActionResult> Delete(int regionId, CancellationToken ct)
    {
        if (RequireAdmin() is IActionResult forbid) return forbid;

        var region = await _db.Regions.FirstOrDefaultAsync(x => x.Id == regionId, ct);
        if (region is null)
            return NotFound(new { message = "المنطقة غير موجودة." });

        var hasDevices = await _db.TerminalRegionMaps.AsNoTracking().AnyAsync(x => x.RegionId == regionId, ct);
        if (hasDevices)
            return Conflict(new { message = "لا يمكن حذف المنطقة لأنها مرتبطة بأجهزة." });

        var name = region.Name;
        _db.Regions.Remove(region);
        await _db.SaveChangesAsync(ct);

        await _activity.LogAsync(
            action: "Region.Deleted",
            entityType: "Region",
            entityId: regionId.ToString(),
            summary: $"تم حذف المنطقة {regionId} ({name}).",
            details: new { regionId, regionName = name },
            ct: ct
        );

        return Ok(new { message = "تم حذف المنطقة." });
    }

    // ✅ GET: /api/regions/{regionId}/terminals
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
