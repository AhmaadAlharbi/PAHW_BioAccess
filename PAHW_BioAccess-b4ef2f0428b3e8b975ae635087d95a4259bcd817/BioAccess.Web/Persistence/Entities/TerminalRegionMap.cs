namespace Terminals.Web.Persistence.Entities;

public class TerminalRegionMap
{
    public string TerminalId { get; set; } = default!;  // PK

    public int RegionId { get; set; }                   // FK

    // âœ… Navigation Property (Ù‡Ø°Ø§ Ø§Ù„Ù„ÙŠ ÙƒØ§Ù† Ù†Ø§Ù‚Øµ)

    // âœ… Ù‡Ø°Ø§ Ø§Ù„Ù„ÙŠ ÙƒØ§Ù† Ù†Ø§Ù‚Øµ Ø¹Ù†Ø¯Ùƒ (Ø¹Ø´Ø§Ù† .HasOne(x=>x.Region) ÙŠØ´ØªØºÙ„)
    public Region? Region { get; set; }
}
