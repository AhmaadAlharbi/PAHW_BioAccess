namespace BioAccess.Web.DTOs;

public class TerminalWithRegionDto
{
    public string TerminalId { get; set; } = "";
    public string TerminalName { get; set; } = "";

    public int? RegionId { get; set; }
    public string? RegionName { get; set; }
}
