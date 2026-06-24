namespace ExcPanel.TransferAgent.Models;

public class StorageStatusResponse
{
    public string RootPath { get; set; } = string.Empty;
    public bool Exists { get; set; }
    public long? TotalBytes { get; set; }
    public long? FreeBytes { get; set; }
    public long? UsedBytes { get; set; }
    public double? TotalGb { get; set; }
    public double? FreeGb { get; set; }
    public double? UsedGb { get; set; }
}
