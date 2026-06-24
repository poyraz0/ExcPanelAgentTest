namespace ExcPanel.TransferAgent.Models.Setup;

public class SetupStorageConfig
{
    public string DiskPath { get; set; } = string.Empty;
    public string MountPath { get; set; } = string.Empty;
    public string FileSystem { get; set; } = "ext4";
}
