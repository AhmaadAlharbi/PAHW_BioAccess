namespace Terminals.Web.DTOs;

public sealed class BulkOpResultDto
{
    public int Ok { get; set; }
    public int Fail { get; set; }
    public string? Message { get; set; }
}
