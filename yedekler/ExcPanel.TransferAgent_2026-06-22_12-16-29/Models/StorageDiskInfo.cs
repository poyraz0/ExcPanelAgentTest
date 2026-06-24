namespace ExcPanel.TransferAgent.Models;

public class StorageDiskInfo
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public double SizeGb { get; set; }
    public string? Serial { get; set; }
    public string? Wwn { get; set; }
    public string Type { get; set; } = string.Empty;
    public bool IsMounted { get; set; }
    public string? MountPoint { get; set; }
    public bool HasPartitions { get; set; }
    public bool IsSystemDisk { get; set; }
    public bool Recommended { get; set; }
    public string? Reason { get; set; }
}
