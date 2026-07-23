namespace Terminals.Web.Persistence.Entities;

public sealed class ActivityLog
{
    public int Id { get; set; }
    public DateTime CreatedAt { get; set; }

    // "User" | "System"
    public string ActorType { get; set; } = "User";
    public int? ActorEmployeeId { get; set; }
    public string? ActorName { get; set; }

    public string Action { get; set; } = "";
    public string EntityType { get; set; } = "";
    public string? EntityId { get; set; }

    public string Summary { get; set; } = "";

    public string? DetailsJson { get; set; }
    public string? Severity { get; set; }
}

