namespace ExcPanel.TransferAgent.Models.Domain;

public class DomainJoinResponse
{
    public bool Joined { get; set; }
    public bool Skipped { get; set; }
    public string? JoinedDomain { get; set; }
    public List<string> CompletedSteps { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}
