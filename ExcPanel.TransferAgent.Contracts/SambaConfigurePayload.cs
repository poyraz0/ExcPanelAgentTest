namespace ExcPanel.TransferAgent.Contracts;

public class SambaConfigurePayload
{
    public string ShareName { get; set; } = string.Empty;
    public string StorageRoot { get; set; } = string.Empty;
    public string ConfigFilePath { get; set; } = string.Empty;
    public string MainConfigPath { get; set; } = "/etc/samba/smb.conf";
    public string RequiredAdGroup { get; set; } = string.Empty;
    public bool AllowGuest { get; set; }
    public string SmbMinProtocol { get; set; } = "SMB2";
    public string SmbMaxProtocol { get; set; } = "SMB3";
    public string OwnerUser { get; set; } = string.Empty;
    public string OwnerGroup { get; set; } = string.Empty;
}
