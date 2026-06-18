using System.Text.Json;
using BioAccess.Web.Persistence;
using BioAccess.Web.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BioAccess.Web.Services.Activity;

public sealed class ActivityLogService : IActivityLogService
{
    private readonly LocalAppDbContext _db;
    private readonly IHttpContextAccessor _http;
    private readonly ILogger<ActivityLogService> _logger;

    public ActivityLogService(LocalAppDbContext db, IHttpContextAccessor http, ILogger<ActivityLogService> logger)
    {
        _db = db;
        _http = http;
        _logger = logger;
    }

    public async Task LogAsync(
        string action,
        string entityType,
        string? entityId,
        string summary,
        object? details = null,
        string? severity = null,
        CancellationToken ct = default)
    {
        var ctx = _http.HttpContext;
        var actorName = ctx?.Session.GetString("EmpName");
        var actorEmpIdStr = ctx?.Session.GetString("EmpId");
        int? actorEmpId = null;
        if (int.TryParse(actorEmpIdStr, out var parsed)) actorEmpId = parsed;

        await WriteAsync(new ActivityLog
        {
            CreatedAt = DateTime.Now,
            ActorType = "User",
            ActorEmployeeId = actorEmpId,
            ActorName = string.IsNullOrWhiteSpace(actorName) ? null : actorName,
            Action = action,
            EntityType = entityType,
            EntityId = string.IsNullOrWhiteSpace(entityId) ? null : entityId,
            Summary = summary,
            DetailsJson = details == null ? null : JsonSerializer.Serialize(details),
            Severity = string.IsNullOrWhiteSpace(severity) ? null : severity
        }, ct);
    }

    public async Task LogSystemAsync(
        string actorName,
        string action,
        string entityType,
        string? entityId,
        string summary,
        object? details = null,
        string? severity = null,
        CancellationToken ct = default)
    {
        await WriteAsync(new ActivityLog
        {
            CreatedAt = DateTime.Now,
            ActorType = "System",
            ActorEmployeeId = null,
            ActorName = actorName,
            Action = action,
            EntityType = entityType,
            EntityId = string.IsNullOrWhiteSpace(entityId) ? null : entityId,
            Summary = summary,
            DetailsJson = details == null ? null : JsonSerializer.Serialize(details),
            Severity = string.IsNullOrWhiteSpace(severity) ? null : severity
        }, ct);
    }

    public Task<List<ActivityLog>> GetLatestAsync(int take, CancellationToken ct = default)
        => _db.ActivityLogs
            .AsNoTracking()
            .OrderByDescending(x => x.CreatedAt)
            .Take(take)
            .ToListAsync(ct);

    private async Task WriteAsync(ActivityLog log, CancellationToken ct)
    {
        // Keep it simple: logging should never break the user flow.
        try
        {
            _db.ActivityLogs.Add(log);
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write activity log.");
        }
    }
}
