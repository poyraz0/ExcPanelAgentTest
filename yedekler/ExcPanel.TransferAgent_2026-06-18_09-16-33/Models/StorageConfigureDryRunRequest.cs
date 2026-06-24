namespace ExcPanel.TransferAgent.Models;

public class StorageConfigureDryRunRequest
{
    public string DiskPath { get; set; } = string.Empty;
    public string? TargetMountPath { get; set; }
}
