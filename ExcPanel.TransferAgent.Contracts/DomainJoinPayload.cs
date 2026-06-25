namespace ExcPanel.TransferAgent.Contracts;

public class DomainJoinPayload
{
    public string DnsDomain { get; set; } = string.Empty;
    public string Realm { get; set; } = string.Empty;
    public string Workgroup { get; set; } = string.Empty;
    public string DomainController { get; set; } = string.Empty;
    public string DomainControllerIp { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? ComputerName { get; set; }
    public string? ComputerOu { get; set; }
    public string RequiredAdGroup { get; set; } = string.Empty;
    public string Krb5ConfPath { get; set; } = "/etc/krb5.conf";
    public string MainConfigPath { get; set; } = "/etc/samba/smb.conf";
    public string HostsFilePath { get; set; } = "/etc/hosts";
}
