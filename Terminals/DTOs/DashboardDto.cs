namespace Terminals.Web.DTOs;

public sealed class DashboardDto
{
    public int RegionsCount { get; set; }
    public int MappingsCount { get; set; }
    public int ActiveDelegationsCount { get; set; }
    public List<ActivityLogDto> RecentActivities { get; set; } = new();
    public List<DelegationDto> LatestDelegations { get; set; } = new();
    public string DelegationsFilter { get; set; } = "Active";
}

public sealed class ActivityLogDto
{
    public string ActionType { get; set; } = "";
    public string Summary { get; set; } = "";
    public string PerformedBy { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

public sealed class DelegationDto
{
    public int EmployeeId { get; set; }
    public string EmployeeName { get; set; } = "";
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string Status { get; set; } = "";
    public int TerminalsCount { get; set; }
    public string RegionText { get; set; } = "";
}
