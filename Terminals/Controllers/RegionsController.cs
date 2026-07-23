using Terminals.Web.Contracts;
using Terminals.Web.DTOs.Api;
using Terminals.Web.Services.Activity;
using Terminals.Web.Services.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Terminals.Web.Controllers;

[ApiController]
[Route("api/regions")]
[Authorize(AuthenticationSchemes = "Bearer")]
public sealed class RegionsController : ControllerBase
{
    private readonly IRegionService _regions;
    private readonly IActivityLogService _activity;
    private readonly ICurrentUser _currentUser;

    public RegionsController(IRegionService regions, IActivityLogService activity, ICurrentUser currentUser)
    {
        _regions = regions;
        _activity = activity;
        _currentUser = currentUser;
    }

    // GET: /api/regions
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        if (!_currentUser.IsAdmin)
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse.Fail("غير مصرح"));

        var regions = await _regions.GetRegionsAsync(ct);
        return Ok(ApiResponse<IReadOnlyList<RegionDto>>.Ok(regions));
    }

    // POST: /api/regions
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateRegionRequest req, CancellationToken ct)
    {
        if (!_currentUser.IsAdmin)
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse.Fail("غير مصرح"));

        try
        {
            await _regions.CreateRegionAsync(req.Name, ct);
        }
        catch (InvalidOperationException ex)
        {
            return ex.Message.Contains("موجودة")
                ? Conflict(ApiResponse.Fail(ex.Message))
                : BadRequest(ApiResponse.Fail(ex.Message));
        }

        var all = await _regions.GetRegionsAsync(ct);
        var created = all.FirstOrDefault(r => r.Name == req.Name.Trim());

        await _activity.LogAsync(
            action: "Region.Created",
            entityType: "Region",
            entityId: created?.Id.ToString(),
            summary: $"تم إنشاء منطقة جديدة: {created?.Id} ({req.Name.Trim()}).",
            details: new { regionId = created?.Id, regionName = req.Name.Trim() },
            ct: ct);

        return Ok(ApiResponse<RegionDto>.Ok(created!));
    }

    // PUT: /api/regions/{regionId}
    [HttpPut("{regionId:int}")]
    public async Task<IActionResult> Rename(int regionId, [FromBody] RenameRegionRequest req, CancellationToken ct)
    {
        if (!_currentUser.IsAdmin)
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse.Fail("غير مصرح"));

        try
        {
            await _regions.RenameRegionAsync(regionId, req.Name, ct);
        }
        catch (InvalidOperationException ex)
        {
            return ex.Message.Contains("غير موجودة")
                ? NotFound(ApiResponse.Fail(ex.Message))
                : BadRequest(ApiResponse.Fail(ex.Message));
        }

        await _activity.LogAsync(
            action: "Region.Renamed",
            entityType: "Region",
            entityId: regionId.ToString(),
            summary: $"تم تعديل اسم المنطقة {regionId} إلى \"{req.Name.Trim()}\".",
            details: new { regionId, newName = req.Name.Trim() },
            ct: ct);

        return Ok(ApiResponse.Ok());
    }

    // DELETE: /api/regions/{regionId}
    [HttpDelete("{regionId:int}")]
    public async Task<IActionResult> Delete(int regionId, CancellationToken ct)
    {
        if (!_currentUser.IsAdmin)
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse.Fail("غير مصرح"));

        try
        {
            await _regions.DeleteRegionAsync(regionId, ct);
        }
        catch (InvalidOperationException ex)
        {
            return ex.Message.Contains("غير موجودة")
                ? NotFound(ApiResponse.Fail(ex.Message))
                : Conflict(ApiResponse.Fail(ex.Message));
        }

        await _activity.LogAsync(
            action: "Region.Deleted",
            entityType: "Region",
            entityId: regionId.ToString(),
            summary: $"تم حذف المنطقة {regionId}.",
            details: new { regionId },
            ct: ct);

        return Ok(ApiResponse.Ok());
    }

    // GET: /api/regions/{regionId}/terminals
    [HttpGet("{regionId:int}/terminals")]
    public async Task<IActionResult> GetRegionTerminals(int regionId, CancellationToken ct)
    {
        if (!_currentUser.IsAdmin)
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse.Fail("غير مصرح"));

        var terminals = await _regions.GetRegionTerminalsAsync(regionId, ct);
        return Ok(ApiResponse<IReadOnlyList<TerminalRegionDto>>.Ok(terminals));
    }
}
