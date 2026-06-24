namespace ExcPanel.TransferAgent.Models.Domain;

public class DomainStatusResponse
{
    public bool Supported { get; set; }
    public bool DomainJoined { get; set; }
    public string? JoinedDomain { get; set; }
    public string? JoinedRealm { get; set; }
    public bool DnsResolvable { get; set; }
    public bool DcReachable { get; set; }
    public bool SmbdRunning { get; set; }
    public bool WinbindRunning { get; set; }
    public bool RequiredAdGroupResolved { get; set; }
    public string? RequiredAdGroup { get; set; }
    public List<string> Warnings { get; set; } = [];
}
