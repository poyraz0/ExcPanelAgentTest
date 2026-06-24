namespace ExcPanel.TransferAgent.Contracts;

public class DomainJoinResultData
{
    public bool Joined { get; set; }
    public bool Skipped { get; set; }
    public string? JoinedDomain { get; set; }
    public bool TestJoinSucceeded { get; set; }
    public bool DcPingSucceeded { get; set; }
    public bool RequiredAdGroupResolved { get; set; }
    public List<string> BackupPaths { get; set; } = [];
}
