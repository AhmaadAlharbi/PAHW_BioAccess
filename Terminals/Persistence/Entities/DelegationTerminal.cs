namespace Terminals.Web.Persistence.Entities;

public class DelegationTerminal
{
    public int Id { get; set; }

    public int DelegationId { get; set; }
    public Delegation? Delegation { get; set; }

    public string TerminalId { get; set; } = default!;
    public bool WasAssignedBefore { get; set; }
    public bool IsManuallyRemoved { get; set; }
}