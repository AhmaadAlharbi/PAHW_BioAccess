namespace BioAccess.Web.DTOs;

public sealed class DashboardDto
{
    public int RegionsCount { get; set; }
    public int MappingsCount { get; set; }
    public int ActiveDelegationsCount { get; set; }
    public List<ActivityLogDto> RecentActivities { get; set; } = new();
}

public sealed class ActivityLogDto
{
    public string ActionType { get; set; } = "";
    public string Summary { get; set; } = "";
    public string PerformedBy { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}
