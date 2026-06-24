namespace ExcPanel.TransferAgent.Models;

public class ConfigureStorageRequest
{
    public string DiskPath { get; set; } = string.Empty;
    public string MountPath { get; set; } = string.Empty;
    public string FileSystem { get; set; } = "ext4";
    public string ConfirmationCode { get; set; } = string.Empty;
}
