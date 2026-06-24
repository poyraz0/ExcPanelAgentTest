namespace ExcPanel.TransferAgent.Models;

public class StorageConfigureDryRunRequest
{
    public string DiskPath { get; set; } = string.Empty;
    public string? TargetMountPath { get; set; }
    public string? MountPath { get; set; }

    public string? ResolveMountPath()
    {
        if (!string.IsNullOrWhiteSpace(TargetMountPath))
        {
            return TargetMountPath.Trim();
        }

        if (!string.IsNullOrWhiteSpace(MountPath))
        {
            return MountPath.Trim();
        }

        return null;
    }
}
