namespace ExcPanel.TransferAgent.Contracts;

public class StorageConfigureResultData
{
    public string DiskPath { get; set; } = string.Empty;
    public string PartitionPath { get; set; } = string.Empty;
    public string MountPath { get; set; } = string.Empty;
    public string FileSystem { get; set; } = string.Empty;
    public string Uuid { get; set; } = string.Empty;
    public IReadOnlyList<string> CreatedDirectories { get; set; } = Array.Empty<string>();
    public bool FstabUpdated { get; set; }
    public bool Mounted { get; set; }
}
