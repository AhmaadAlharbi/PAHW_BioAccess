namespace Terminals.Web.Persistence.Entities;

public class AllowedUser
{
    public int Id { get; set; }

    // Ø§Ù„Ø±Ù‚Ù… Ø§Ù„ÙˆØ¸ÙŠÙÙŠ (Ù‡Ø°Ø§ Ø£Ù‡Ù… Ø´ÙŠ)
    public int EmployeeId { get; set; }

    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Department { get; set; } = "";

    public bool IsActive { get; set; } = true;

    // Ø§Ø®ØªÙŠØ§Ø±ÙŠ Ù„Ù„Ù…Ù†ØªØ¯Ø¨ÙŠÙ† (Ø¥Ø°Ø§ ØªØ¨ÙŠ)
    public DateTime? ValidUntil { get; set; }

    // Ø§Ø®ØªÙŠØ§Ø±ÙŠ: Ù…Ù†Ùˆ ÙŠÙ‚Ø¯Ø± ÙŠØ¶ÙŠÙ
    public bool IsAdmin { get; set; } = false;
}