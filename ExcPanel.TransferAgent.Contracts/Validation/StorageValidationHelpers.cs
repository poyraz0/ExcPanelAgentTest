using System.Text.RegularExpressions;

namespace ExcPanel.TransferAgent.Contracts.Validation;

public static partial class StorageValidationHelpers
{
    public const string AllowedMountPath = "/data/excpanel-transfer";

    private static readonly HashSet<string> ForbiddenMountPaths = new(StringComparer.Ordinal)
    {
        "/",
        "/boot",
        "/home",
        "/etc",
        "/var",
        "/usr",
        "/opt",
        "/tmp"
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

    public static string ResolvePartitionPath(string diskPath)
    {
        if (NvmeDiskRegex().IsMatch(diskPath))
        {
            return $"{diskPath}p1";
        }

        return $"{diskPath}1";
    }

    public static IReadOnlyList<string> ValidateMountPath(string? mountPath, bool allowExistingContent = false)
    {
        var errors = new List<string>();
        var normalized = mountPath?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(normalized))
        {
            errors.Add("mountPath is required.");
            return errors;
        }

        if (!string.Equals(normalized, AllowedMountPath, StringComparison.Ordinal))
        {
            errors.Add($"mountPath must be exactly '{AllowedMountPath}'.");
        }

        if (ForbiddenMountPaths.Contains(normalized))
        {
            errors.Add($"mountPath '{normalized}' is not allowed.");
        }

        if (normalized.Contains("..", StringComparison.Ordinal))
        {
            errors.Add("mountPath must not contain path traversal sequences.");
        }

        if (Path.IsPathRooted(normalized) && (normalized.Contains('\\') || normalized.Contains(';')))
        {
            errors.Add("mountPath contains invalid characters.");
        }

        try
        {
            if (Directory.Exists(normalized))
            {
                var fileInfo = new FileInfo(normalized);
                if (fileInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    errors.Add("mountPath must not be a symlink.");
                }
                else if (!allowExistingContent && Directory.EnumerateFileSystemEntries(normalized).Any())
                {
                    errors.Add($"mountPath '{normalized}' already exists and is not empty.");
                }
            }
            else
            {
                var parent = Path.GetDirectoryName(normalized);
                while (!string.IsNullOrEmpty(parent) && parent != "/")
                {
                    if (Directory.Exists(parent))
                    {
                        var parentInfo = new FileInfo(parent);
                        if (parentInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
                        {
                            errors.Add("mountPath resolves through a symlink.");
                            break;
                        }

                        break;
                    }

                    parent = Path.GetDirectoryName(parent);
                }
            }
        }
        catch
        {
            errors.Add("mountPath could not be validated.");
        }

        return errors;
    }

    public static IReadOnlyList<string> ValidateFileSystem(string? fileSystem)
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

    public static string NormalizeDiskIdentity(string? serial, string? wwn)
    {
        if (!string.IsNullOrWhiteSpace(serial))
        {
            return serial.Trim();
        }

        if (!string.IsNullOrWhiteSpace(wwn))
        {
            return wwn.Trim();
        }

        return string.Empty;
    }
}
