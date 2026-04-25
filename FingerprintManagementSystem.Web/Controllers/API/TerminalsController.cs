using FingerprintManagementSystem.ApiAdapter.Alpeta;
using FingerprintManagementSystem.ApiAdapter.Persistence;
using FingerprintManagementSystem.ApiAdapter.Persistence.Entities;
using FingerprintManagementSystem.Contracts.DTOs;
using FingerprintManagementSystem.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace FingerprintManagementSystem.Web.Controllers.Api;

[ApiController]
[Route("api/terminals")]
public class TerminalsController : ControllerBase
{
    private readonly LocalAppDbContext _db;
    private readonly AlpetaClient _alpeta;
    private readonly IActivityLogService _activity;

    public TerminalsController(LocalAppDbContext db, AlpetaClient alpeta, IActivityLogService activity)
    {
        _db = db;
        _alpeta = alpeta;
        _activity = activity;
    }

    private IActionResult? RequireAdmin()
    {
        var isAdmin = HttpContext.Session.GetString("IsAdmin") == "1";
        return isAdmin ? null : Forbid();
    }

    // 🔹 جلب المناطق (للبحث / dropdown)
    [HttpGet("regions")]
    public async Task<IActionResult> GetRegions(CancellationToken ct)
    {
        if (RequireAdmin() is IActionResult forbid) return forbid;

        var regions = await _db.Regions
            .AsNoTracking()
            .OrderBy(r => r.Id)
            .Select(r => new { r.Id, r.Name })
            .ToListAsync(ct);

        return Ok(new { total = regions.Count, regions });
    }

    // 🔹 جلب جميع أجهزة Alpeta + المنطقة الحالية إن وجدت (مصدر الأجهزة: Alpeta، ومصدر الربط: DB)
    [HttpGet]
    public async Task<IActionResult> GetAllDevices(CancellationToken ct)
    {
        if (RequireAdmin() is IActionResult forbid) return forbid;

        var devices = await _alpeta.GetAllDevicesAsync(ct);

        var regionNameById = await _db.Regions
            .AsNoTracking()
            .ToDictionaryAsync(x => x.Id, x => x.Name, ct);

        var maps = await _db.TerminalRegionMaps
            .AsNoTracking()
            .ToListAsync(ct);

        var mapByTerminalId = maps.ToDictionary(x => x.TerminalId, x => x.RegionId, StringComparer.OrdinalIgnoreCase);
        var deviceIdSet = new HashSet<string>(devices.Select(x => x.DeviceId?.Trim() ?? ""), StringComparer.OrdinalIgnoreCase);

        var rows = devices
            .Where(d => !string.IsNullOrWhiteSpace(d.DeviceId))
            .Select(d =>
            {
                var id = d.DeviceId.Trim();
                mapByTerminalId.TryGetValue(id, out var regionId);
                regionNameById.TryGetValue(regionId, out var regionName);

                return new
                {
                    deviceId = id,
                    deviceName = d.DeviceName,
                    location = d.Location,
                    regionId = regionId == 0 ? (int?)null : regionId,
                    regionName = string.IsNullOrWhiteSpace(regionName) ? null : regionName,
                    status = regionId == 0 ? "Unassigned" : "Assigned"
                };
            })
            .OrderBy(x => x.status == "Unassigned" ? 0 : 1)
            .ThenBy(x => x.regionName ?? "")
            .ThenBy(x => x.deviceName ?? "")
            .ThenBy(x => x.deviceId)
            .ToList();

        var staleMappings = maps.Count(m => !deviceIdSet.Contains(m.TerminalId ?? ""));

        return Ok(new
        {
            total = rows.Count,
            devices = rows,
            staleMappings
        });
    }

    // 🔹 ربط/نقل جهاز إلى منطقة (Upsert)
    [HttpPut("{terminalId}/region")]
    public async Task<IActionResult> AssignToRegion(string terminalId, [FromBody] AssignTerminalRegionRequest req, CancellationToken ct)
    {
        if (RequireAdmin() is IActionResult forbid) return forbid;

        terminalId = (terminalId ?? "").Trim();
        if (string.IsNullOrWhiteSpace(terminalId))
            return BadRequest(new { message = "TerminalId مطلوب." });

        if (req == null || req.RegionId <= 0)
            return BadRequest(new { message = "RegionId غير صحيح." });

        var region = await _db.Regions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == req.RegionId, ct);
        if (region is null)
            return NotFound(new { message = "المنطقة غير موجودة." });

        var row = await _db.TerminalRegionMaps.FirstOrDefaultAsync(x => x.TerminalId == terminalId, ct);
        var oldRegionId = row?.RegionId;
        if (row is null)
            _db.TerminalRegionMaps.Add(new TerminalRegionMap { TerminalId = terminalId, RegionId = req.RegionId });
        else
            row.RegionId = req.RegionId;

        await _db.SaveChangesAsync(ct);

        if (oldRegionId != req.RegionId)
        {
            var action = oldRegionId.HasValue ? "TerminalRegion.Moved" : "TerminalRegion.Assigned";
            await _activity.LogAsync(
                action: action,
                entityType: "TerminalRegionMap",
                entityId: terminalId,
                summary: oldRegionId.HasValue
                    ? $"تم نقل الجهاز {terminalId} من منطقة {oldRegionId.Value} إلى {region.Id} ({region.Name})."
                    : $"تم ربط الجهاز {terminalId} بمنطقة {region.Id} ({region.Name}).",
                details: new { terminalId, oldRegionId, newRegionId = req.RegionId, newRegionName = region.Name },
                ct: ct
            );
        }

        return Ok(new { message = "تم حفظ ربط الجهاز بالمنطقة بنجاح." });
    }

    // 🔹 فك ربط جهاز من أي منطقة (يصبح Unassigned)
    [HttpDelete("{terminalId}/region")]
    public async Task<IActionResult> ClearRegion(string terminalId, CancellationToken ct)
    {
        if (RequireAdmin() is IActionResult forbid) return forbid;

        terminalId = (terminalId ?? "").Trim();
        if (string.IsNullOrWhiteSpace(terminalId))
            return BadRequest(new { message = "TerminalId مطلوب." });

        var row = await _db.TerminalRegionMaps.FirstOrDefaultAsync(x => x.TerminalId == terminalId, ct);
        if (row is null)
            return Ok(new { message = "الجهاز غير مرتبط مسبقًا." });

        var oldRegionId = row.RegionId;
        _db.TerminalRegionMaps.Remove(row);
        await _db.SaveChangesAsync(ct);

        await _activity.LogAsync(
            action: "TerminalRegion.Cleared",
            entityType: "TerminalRegionMap",
            entityId: terminalId,
            summary: $"تم فك ربط الجهاز {terminalId} من المنطقة {oldRegionId}.",
            details: new { terminalId, oldRegionId },
            ct: ct
        );

        return Ok(new { message = "تم فك ربط الجهاز بنجاح." });
    }

    public sealed class BulkAssignRequest
    {
        public List<string> TerminalIds { get; set; } = new();
        public int RegionId { get; set; }
    }

    // 🔹 ربط/نقل مجموعة أجهزة إلى منطقة
    [HttpPost("bulk/region")]
    public async Task<IActionResult> BulkAssignToRegion([FromBody] BulkAssignRequest req, CancellationToken ct)
    {
        if (RequireAdmin() is IActionResult forbid) return forbid;

        if (req == null || req.RegionId <= 0)
            return BadRequest(new { message = "RegionId غير صحيح." });

        var ids = (req.TerminalIds ?? new List<string>())
            .Select(x => (x ?? "").Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (ids.Count == 0)
            return BadRequest(new { message = "قائمة الأجهزة فارغة." });

        var region = await _db.Regions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == req.RegionId, ct);
        if (region is null)
            return NotFound(new { message = "المنطقة غير موجودة." });

        var existing = await _db.TerminalRegionMaps
            .Where(x => ids.Contains(x.TerminalId))
            .ToListAsync(ct);

        var existingById = existing.ToDictionary(x => x.TerminalId, x => x, StringComparer.OrdinalIgnoreCase);

        foreach (var id in ids)
        {
            if (existingById.TryGetValue(id, out var row))
                row.RegionId = req.RegionId;
            else
                _db.TerminalRegionMaps.Add(new TerminalRegionMap { TerminalId = id, RegionId = req.RegionId });
        }

        await _db.SaveChangesAsync(ct);

        await _activity.LogAsync(
            action: "TerminalRegion.BulkAssigned",
            entityType: "TerminalRegionMap",
            entityId: region.Id.ToString(),
            summary: $"تم توزيع/نقل {ids.Count} جهاز إلى المنطقة {region.Id} ({region.Name}).",
            details: new { regionId = region.Id, regionName = region.Name, count = ids.Count },
            ct: ct
        );

        return Ok(new BulkOpResultDto
        {
            Ok = ids.Count,
            Fail = 0,
            Message = "تم تحديث توزيع الأجهزة المحددة بنجاح."
        });
    }

    public sealed class BulkClearRequest
    {
        public List<string> TerminalIds { get; set; } = new();
    }

    // 🔹 فك ربط مجموعة أجهزة (تصبح Unassigned)
    [HttpPost("bulk/clear-region")]
    public async Task<IActionResult> BulkClearRegions([FromBody] BulkClearRequest req, CancellationToken ct)
    {
        if (RequireAdmin() is IActionResult forbid) return forbid;

        var ids = (req?.TerminalIds ?? new List<string>())
            .Select(x => (x ?? "").Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (ids.Count == 0)
            return BadRequest(new { message = "قائمة الأجهزة فارغة." });

        var rows = await _db.TerminalRegionMaps
            .Where(x => ids.Contains(x.TerminalId))
            .ToListAsync(ct);

        _db.TerminalRegionMaps.RemoveRange(rows);
        await _db.SaveChangesAsync(ct);

        await _activity.LogAsync(
            action: "TerminalRegion.BulkCleared",
            entityType: "TerminalRegionMap",
            entityId: null,
            summary: $"تم فك ربط {rows.Count} جهاز (Bulk Clear).",
            details: new { requested = ids.Count, cleared = rows.Count },
            ct: ct
        );

        return Ok(new BulkOpResultDto
        {
            Ok = rows.Count,
            Fail = ids.Count - rows.Count,
            Message = "تم فك ربط الأجهزة المحددة."
        });
    }

    [HttpPost("auto-assign-regions")]
    public async Task<IActionResult> AutoAssignRegions(CancellationToken ct)
    {
        if (RequireAdmin() is IActionResult forbid) return forbid;

        // 1) جلب المناطق والخرائط الحالية لتقليل استهلاك قاعدة البيانات
        var regionsList = await _db.Regions.AsNoTracking().ToListAsync(ct);
        var existingMaps = await _db.TerminalRegionMaps.ToDictionaryAsync(x => x.TerminalId, x => x, ct);

        var regionIds = regionsList
            .GroupBy(r => Normalize(r.Name))
            .ToDictionary(g => g.Key, g => g.First().Id);

        int? TryGetRegionId(string regionName)
            => regionIds.TryGetValue(Normalize(regionName), out var id) ? id : null;

        static bool ContainsAny(string normalizedText, params string[] tokens)
            => tokens.Any(t => normalizedText.Contains(Normalize(t), StringComparison.OrdinalIgnoreCase));

        // 2) منطق التوزيع المحدث (يشمل اختلاف الـ spelling + السالمي)
        int? GetRegionIdByTerminal(int terminalId, string terminalName)
        {
            var other = TryGetRegionId("مواقع أخرى");
            if (other is null) return null;

            // المبنى الرئيسي (IDs 3..26)
            if (terminalId >= 3 && terminalId <= 26)
                return TryGetRegionId("المبنى الرئيسي") ?? other;

            var n = Normalize(terminalName);

            // المطلاع
            if (ContainsAny(n, "mutlaa", "mutla", "المطلاع", "مطلاع"))
                return TryGetRegionId("المطلاع") ?? other;

            // برج التحرير
            if (ContainsAny(n, "liberation", "tower", "تحرير", "برج التحرير"))
                return TryGetRegionId("برج التحرير") ?? other;

            // صباح السالم (يغطي: SABAH ELSALEM / SABAH SALIM / صباح السالم)
            if (ContainsAny(n,
                    "sabah al salem", "sabah elsalem", "sabah el salem", "sabah salem", "sabah salim",
                    "صباح السالم", "صباح سالم"))
                return TryGetRegionId("صباح السالم") ?? other;

            // سعد العبدالله
            if (ContainsAny(n, "saad", "سعد"))
                return TryGetRegionId("سعد العبدالله") ?? other;

            // جابر الأحمد
            if (ContainsAny(n, "jaber", "جابر"))
                return TryGetRegionId("جابر الأحمد") ?? other;

            // الصليبية (يغطي: Sulaibia / Sulaibiya)
            if (ContainsAny(n, "sulaibia", "sulaibiya", "sulaibi", "الصليبية", "صليبية"))
                return TryGetRegionId("الصليبية") ?? other;

            // مبارك الكبير (يغطي: Mubarak / Mubark Alkabir)
            if (ContainsAny(n, "mubarak", "mubark", "alkabir", "al kabir", "مبارك الكبير"))
                return TryGetRegionId("مبارك الكبير") ?? other;

            // النهضة
            if (ContainsAny(n, "nahda", "النهضة", "نهضة"))
                return TryGetRegionId("النهضة") ?? other;

            // غرب الجليب
            if (ContainsAny(n, "west jleeb", "west jleib", "jleeb", "غرب الجليب"))
                return TryGetRegionId("غرب الجليب") ?? other;

            // السالمي (جديد) — يغطي: Salmi / السالمي
            if (ContainsAny(n, "salmi", "السالمي", "سالمي"))
                return TryGetRegionId("السالمي") ?? other;

            // الجهراء (حكومة مول / تيماء) — يغطي: Jahra/Jahrah
            if (ContainsAny(n, "jahra", "jahrah", "جهراء", "الجهراء"))
            {
                if (ContainsAny(n, "taima", "tayma", "تيماء"))
                    return TryGetRegionId("الجهراء - تيماء") ?? other;

                return TryGetRegionId("الجهراء - حكومة مول") ?? other;
            }

            // القرين - حكومة مول
            if (ContainsAny(n, "qurain", "قرين", "القرين"))
                return TryGetRegionId("القرين - حكومة مول") ?? other;

            return other;
        }

        // 3) جلب الأجهزة من Alpeta API
        var devices = await _alpeta.GetAllDevicesAsync(ct);

        int inserted = 0, updated = 0, skippedInvalidId = 0;

        foreach (var d in devices)
        {
            if (!int.TryParse(d.DeviceId, out var tid))
            {
                skippedInvalidId++;
                continue;
            }

            var targetRegionId = GetRegionIdByTerminal(tid, d.DeviceName);
            if (targetRegionId == null) continue;

            if (existingMaps.TryGetValue(d.DeviceId, out var existingMap))
            {
                if (existingMap.RegionId != targetRegionId.Value)
                {
                    existingMap.RegionId = targetRegionId.Value;
                    updated++;
                }
            }
            else
            {
                _db.TerminalRegionMaps.Add(new TerminalRegionMap
                {
                    TerminalId = d.DeviceId,
                    RegionId = targetRegionId.Value
                });
                inserted++;
            }
        }

        await _db.SaveChangesAsync(ct);

        var total = devices.Count;
        var skipped = Math.Max(0, total - inserted - updated - skippedInvalidId);
        await _activity.LogAsync(
            action: "TerminalRegion.AutoAssign",
            entityType: "TerminalRegionMap",
            entityId: null,
            summary: $"تم تشغيل Auto-Assign: inserted={inserted}, updated={updated}, skipped={skipped}, invalidIds={skippedInvalidId} (total={total}).",
            details: new { totalDevices = total, inserted, updated, skipped, invalidDeviceIds = skippedInvalidId },
            ct: ct
        );

        return Ok(new
        {
            message = "تم تحديث توزيع الأجهزة بنجاح",
            totalDevices = devices.Count,
            // Backward/forward compatibility for existing UI code.
            inserted,
            updated,
            newlyAssigned = inserted,
            updatedRegions = updated,
            invalidDeviceIds = skippedInvalidId
        });
    }

    // 🔹 Normalize للأسماء
    private static string Normalize(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;

        s = s.Trim().ToLowerInvariant();
        var sb = new StringBuilder();

        foreach (var ch in s)
        {
            if (char.IsLetterOrDigit(ch) || ch == ' ' || (ch >= 0x0600 && ch <= 0x06FF))
                sb.Append(ch);
            else
                sb.Append(' ');
        }

        return string.Join(' ',
            sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
