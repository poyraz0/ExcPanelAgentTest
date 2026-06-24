namespace ExcPanel.TransferAgent.Models;

public class StorageDiskDiscoveryResponse
{
    public bool Supported { get; set; }
    public string Platform { get; set; } = string.Empty;
    public IReadOnlyList<StorageDiskInfo> Disks { get; set; } = Array.Empty<StorageDiskInfo>();
    public string? Message { get; set; }
}
