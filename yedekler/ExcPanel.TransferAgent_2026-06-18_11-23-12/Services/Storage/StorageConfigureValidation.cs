using System.Text.RegularExpressions;
using ExcPanel.TransferAgent.Contracts;
using ExcPanel.TransferAgent.Contracts.Validation;
using ExcPanel.TransferAgent.Models;

namespace ExcPanel.TransferAgent.Services.Storage;

public static partial class StorageConfigureValidation
{
    [GeneratedRegex(@"^/dev/sd[a-z]+$", RegexOptions.CultureInvariant)]
    private static partial Regex SdDiskRegex();

    [GeneratedRegex(@"^/dev/vd[a-z]+$", RegexOptions.CultureInvariant)]
    private static partial Regex VdDiskRegex();

    [GeneratedRegex(@"^/dev/nvme[0-9]+n[0-9]+$", RegexOptions.CultureInvariant)]
    private static partial Regex NvmeDiskRegex();

    public static bool IsValidWholeDiskPath(string diskPath) =>
        StorageValidationHelpers.IsValidWholeDiskPath(diskPath);

    public static bool IsPartitionPath(string diskPath) =>
        StorageValidationHelpers.IsPartitionPath(diskPath);

    public static List<string> ValidateMountPath(string? mountPath, bool allowExistingContent = false) =>
        StorageValidationHelpers.ValidateMountPath(mountPath, allowExistingContent).ToList();

    public static List<string> ValidateFileSystem(string? fileSystem) =>
        StorageValidationHelpers.ValidateFileSystem(fileSystem).ToList();

    public static bool IsStorageResumeScenario(StorageDiskInfo disk, string diskPath, string mountPath)
    {
        if (disk.IsSystemDisk)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(disk.MountPoint) &&
            !string.Equals(disk.MountPoint, mountPath, StringComparison.Ordinal))
        {
            return false;
        }

        if (!OperatingSystem.IsLinux())
        {
            return disk.HasPartitions;
        }

        var partitionPath = ResolvePartitionPath(diskPath);
        return File.Exists(partitionPath);
    }

    public static List<string> ValidateDiskSelection(
        StorageDiskInfo? selectedDisk,
        string diskPath,
        string mountPath)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(diskPath))
        {
            errors.Add("diskPath is required.");
            return errors;
        }

        if (IsPartitionPath(diskPath))
        {
            errors.Add($"diskPath '{diskPath}' appears to be a partition path. Only whole disks are accepted.");
            return errors;
        }

        if (!IsValidWholeDiskPath(diskPath))
        {
            errors.Add($"diskPath '{diskPath}' is not a supported whole-disk path.");
            return errors;
        }

        if (selectedDisk is null)
        {
            errors.Add($"Disk '{diskPath}' was not found.");
            return errors;
        }

        if (selectedDisk.IsSystemDisk)
        {
            errors.Add("Selected disk is a system disk and cannot be configured.");
            return errors;
        }

        if (IsStorageResumeScenario(selectedDisk, diskPath, mountPath))
        {
            return errors;
        }

        if (selectedDisk.IsMounted)
        {
            errors.Add("Selected disk is mounted and is not suitable for configuration.");
        }

        if (selectedDisk.HasPartitions)
        {
            errors.Add("Selected disk already has partitions or LVM volumes.");
        }

        if (!selectedDisk.Recommended)
        {
            errors.Add("Selected disk is not recommended for storage configuration.");
        }

        return errors;
    }

    public static string ResolvePartitionPath(string diskPath) =>
        StorageValidationHelpers.ResolvePartitionPath(diskPath);

    public static StorageConfirmationIdentity BuildConfirmationIdentity(StorageDiskInfo disk, string mountPath) =>
        new()
        {
            DiskPath = disk.Path,
            DiskSerialOrWwn = StorageValidationHelpers.NormalizeDiskIdentity(disk.Serial, disk.Wwn),
            SizeBytes = disk.SizeBytes,
            MountPath = mountPath
        };
}
