namespace Terminals.Web.Persistence.Entities;

public class Delegation
{
    public int Id { get; set; }

    public int EmployeeId { get; set; }

    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }

    // Scheduled | Active | Expired
    public string Status { get; set; } = "Scheduled";

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? ActivatedAt { get; set; }
    public DateTime? ExpiredAt { get; set; }

    public List<DelegationTerminal> Terminals { get; set; } = new();
}