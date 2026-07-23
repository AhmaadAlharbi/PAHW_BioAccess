namespace Terminals.Web.DTOs;

public class EmployeeDto
{
    public int EmployeeId { get; set; }                 // Ø§Ù„Ø±Ù‚Ù… Ø§Ù„ÙˆØ¸ÙŠÙÙŠ
    public string? CivilId { get; set; }                // Ù†Ø®Ù„ÙŠÙ‡ Ø§Ø®ØªÙŠØ§Ø±ÙŠ Ù„Ù„Ù…Ø³ØªÙ‚Ø¨Ù„
    public string FullNameAr { get; set; } = string.Empty;
    public string? Department { get; set; }
    public string? JobTitle { get; set; }
}
