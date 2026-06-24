using System.Text.RegularExpressions;
using ExcPanel.TransferAgent.Models;

namespace ExcPanel.TransferAgent.Services.Storage;

public static partial class StorageConfigureValidation
{
    private static readonly HashSet<string> ForbiddenMountPaths = new(StringComparer.Ordinal)
    {
        "/",
        "/boot",
        "/home",
        "/var"
    };

    [GeneratedRegex(@"^/dev/sd[a-z]+$", RegexOptions.CultureInvariant)]
    private static partial Regex SdDiskRegex();

    [GeneratedRegex(@"^/dev/vd[a-z]+$", RegexOptions.CultureInvariant)]
    private static partial Regex VdDiskRegex();

    [GeneratedRegex(@"^/dev/nvme[0-9]+n[0-9]+$", RegexOptions.CultureInvariant)]
    private static partial Regex NvmeDiskRegex();

    public static bool IsValidWholeDiskPath(string diskPath) =>
        SdDiskRegex().IsMatch(diskPath) ||
        VdDiskRegex().IsMatch(diskPath) ||
        NvmeDiskRegex().IsMatch(diskPath);

    public static bool IsPartitionPath(string diskPath) =>
        !string.IsNullOrWhiteSpace(diskPath) && !IsValidWholeDiskPath(diskPath.Trim());

    public static string BuildConfirmationCode(StorageDiskInfo disk) =>
        $"FORMAT-{disk.Name.ToUpperInvariant()}-{(int)Math.Round(disk.SizeGb)}G";

    public static List<string> ValidateMountPath(string? mountPath)
    {
        var errors = new List<string>();
        var normalized = mountPath?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(normalized))
        {
            errors.Add("mountPath is required.");
            return errors;
        }

        if (!normalized.StartsWith("/data", StringComparison.Ordinal))
        {
            errors.Add("mountPath must start with /data.");
        }

        if (ForbiddenMountPaths.Contains(normalized))
        {
            errors.Add($"mountPath '{normalized}' is not allowed.");
        }

        if (Directory.Exists(normalized) && Directory.EnumerateFileSystemEntries(normalized).Any())
        {
            errors.Add($"mountPath '{normalized}' already exists and is not empty.");
        }

        return errors;
    }

    public static List<string> ValidateFileSystem(string? fileSystem)
    {
        if (string.IsNullOrWhiteSpace(fileSystem))
        {
            return ["fileSystem is required."];
        }

        if (!string.Equals(fileSystem.Trim(), "ext4", StringComparison.OrdinalIgnoreCase))
        {
            return ["Only ext4 fileSystem is supported."];
        }

        return [];
    }

    public static List<string> ValidateDiskSelection(StorageDiskInfo? selectedDisk, string diskPath)
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

    public static string ResolvePartitionPath(string diskPath)
    {
        if (NvmeDiskRegex().IsMatch(diskPath))
        {
            return $"{diskPath}p1";
        }

        return $"{diskPath}1";
    }
}
