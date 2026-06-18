namespace BioAccess.Web.Persistence.Entities;

public class AllowedUser
{
    public int Id { get; set; }

    // الرقم الوظيفي (هذا أهم شي)
    public int EmployeeId { get; set; }

    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Department { get; set; } = "";

    public bool IsActive { get; set; } = true;

    // اختياري للمنتدبين (إذا تبي)
    public DateTime? ValidUntil { get; set; }

    // اختياري: منو يقدر يضيف
    public bool IsAdmin { get; set; } = false;
}