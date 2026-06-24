namespace ExcPanel.TransferAgent.Models;

public class StorageConfigureDryRunResponse
{
    public bool Supported { get; set; }
    public bool Valid { get; set; }
    public StorageDiskInfo? Disk { get; set; }
    public string TargetMountPath { get; set; } = string.Empty;
    public string? ConfirmationCode { get; set; }
    public IReadOnlyList<string> PlannedActions { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> ValidationErrors { get; set; } = Array.Empty<string>();
}
