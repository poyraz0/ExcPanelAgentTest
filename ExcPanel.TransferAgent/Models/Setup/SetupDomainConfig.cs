namespace ExcPanel.TransferAgent.Models.Setup;

public class SetupDomainConfig
{
    public string DnsDomain { get; set; } = string.Empty;
    public string Realm { get; set; } = string.Empty;
    public string Workgroup { get; set; } = string.Empty;
    public string DomainController { get; set; } = string.Empty;
    public string DomainControllerIp { get; set; } = string.Empty;
    public string JoinUsername { get; set; } = string.Empty;
    public string? ComputerName { get; set; }
    public string? ComputerOu { get; set; }
}
