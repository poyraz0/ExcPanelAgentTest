namespace ExcPanel.TransferAgent.Models.Domain;

public class DomainTestResponse
{
    public bool DomainJoined { get; set; }
    public bool DcPingSucceeded { get; set; }
    public bool TestJoinSucceeded { get; set; }
    public bool RequiredAdGroupResolved { get; set; }
    public string? Message { get; set; }
}
