namespace ExcPanel.TransferAgent.Contracts;

public class StorageConfigurePayload
{
    public string DiskPath { get; set; } = string.Empty;
    public string ExpectedSerial { get; set; } = string.Empty;
    public long ExpectedSizeBytes { get; set; }
    public string MountPath { get; set; } = string.Empty;
    public string FileSystem { get; set; } = "ext4";
    public string OwnerUser { get; set; } = string.Empty;
    public string OwnerGroup { get; set; } = string.Empty;
    public string DirectoryMode { get; set; } = "0750";
}
