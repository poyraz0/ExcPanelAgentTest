namespace ExcPanel.TransferAgent.Models.Domain;

public class DomainPrecheckResponse
{
    public bool Ready { get; set; }
    public bool AlreadyJoined { get; set; }
    public List<DomainPrecheckItem> Checks { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}
