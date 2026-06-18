namespace BioAccess.Web.Persistence.Entities;

public class TerminalRegionMap
{
    public string TerminalId { get; set; } = default!;  // PK

    public int RegionId { get; set; }                   // FK

    // ✅ Navigation Property (هذا اللي كان ناقص)

    // ✅ هذا اللي كان ناقص عندك (عشان .HasOne(x=>x.Region) يشتغل)
    public Region? Region { get; set; }
}
