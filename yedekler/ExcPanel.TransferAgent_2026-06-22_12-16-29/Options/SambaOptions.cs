namespace ExcPanel.TransferAgent.Options;

public class SambaOptions
{
    public const string SectionName = "Samba";

    public bool Enabled { get; set; } = true;
    public string ShareName { get; set; } = "PSTTransfer$";
    public string ServerName { get; set; } = string.Empty;
    public string StorageRoot { get; set; } = "/data/excpanel-transfer";
    public string ConfigFilePath { get; set; } = "/etc/samba/excpanel-transfer.conf";
    public string MainConfigPath { get; set; } = "/etc/samba/smb.conf";
    public string RequiredAdGroup { get; set; } = @"DOGRU\Exchange Trusted Subsystem";
    public bool RequireDomainJoined { get; set; } = true;
    public bool AllowGuest { get; set; }
    public string SmbMinProtocol { get; set; } = "SMB2";
    public string SmbMaxProtocol { get; set; } = "SMB3";
}
