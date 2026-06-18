using BioAccess.Web.Persistence.Entities;

namespace BioAccess.Web.Services.Activity;

public interface IActivityLogService
{
    Task LogAsync(
        string action,
        string entityType,
        string? entityId,
        string summary,
        object? details = null,
        string? severity = null,
        CancellationToken ct = default);

    Task LogSystemAsync(
        string actorName,
        string action,
        string entityType,
        string? entityId,
        string summary,
        object? details = null,
        string? severity = null,
        CancellationToken ct = default);

    Task<List<ActivityLog>> GetLatestAsync(int take, CancellationToken ct = default);
}
