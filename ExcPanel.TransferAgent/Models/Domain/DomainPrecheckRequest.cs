namespace ExcPanel.TransferAgent.Models.Domain;

public class DomainPrecheckRequest
{
    public string DnsDomain { get; set; } = string.Empty;
    public string Realm { get; set; } = string.Empty;
    public string Workgroup { get; set; } = string.Empty;
    public string DomainController { get; set; } = string.Empty;
    public string DomainControllerIp { get; set; } = string.Empty;
    public string? ComputerName { get; set; }
    public string RequiredAdGroup { get; set; } = string.Empty;
}
