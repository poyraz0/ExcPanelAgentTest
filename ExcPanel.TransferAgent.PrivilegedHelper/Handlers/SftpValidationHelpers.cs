using System.Text.Json;
using System.Text.RegularExpressions;
using ExcPanel.TransferAgent.Contracts;

namespace ExcPanel.TransferAgent.PrivilegedHelper.Handlers;

internal static class SftpValidationHelpers
{
    private static readonly Regex UsernameRegex = new("^[a-z0-9_]{3,32}$", RegexOptions.CultureInvariant);

    public static bool IsValidUsername(string? username) =>
        !string.IsNullOrWhiteSpace(username) && UsernameRegex.IsMatch(username.Trim());

    public static bool IsSubPath(string rootPath, string candidatePath)
    {
        var normalizedRoot = Path.GetFullPath(rootPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var normalizedCandidate = Path.GetFullPath(candidatePath);
        return normalizedCandidate.StartsWith(normalizedRoot, StringComparison.Ordinal);
    }

    public static bool IsSymlinkOrReparsePoint(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                return new FileInfo(path).Attributes.HasFlag(FileAttributes.ReparsePoint);
            }

            if (Directory.Exists(path))
            {
                return new DirectoryInfo(path).Attributes.HasFlag(FileAttributes.ReparsePoint);
            }

            return false;
        }
        catch
        {
            return true;
        }
    }

    public static bool ValidateNoSymlinksInPath(string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            var current = Path.IsPathRooted(fullPath) ? Path.GetPathRoot(fullPath)! : string.Empty;
            var segments = fullPath[current.Length..]
                .Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var segment in segments)
            {
                current = Path.Combine(current, segment);
                if (IsSymlinkOrReparsePoint(current))
                {
                    return false;
                }
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool TryResolveJobPath(
        string storageRootPath,
        string jobPath,
        out string normalizedJobPath,
        out string? error)
    {
        normalizedJobPath = string.Empty;
        error = null;

        if (string.IsNullOrWhiteSpace(storageRootPath) || string.IsNullOrWhiteSpace(jobPath))
        {
            error = "Storage root path and job path are required.";
            return false;
        }

        try
        {
            normalizedJobPath = Path.GetFullPath(jobPath);
            var normalizedRoot = Path.GetFullPath(storageRootPath.Trim());

            if (!IsSubPath(normalizedRoot, normalizedJobPath))
            {
                error = "Job path is outside the storage root.";
                return false;
            }

            if (normalizedJobPath.Contains("..", StringComparison.Ordinal))
            {
                error = "Job path contains invalid traversal sequences.";
                return false;
            }

            if (!ValidateNoSymlinksInPath(normalizedJobPath))
            {
                error = "Job path must not contain symlinks.";
                return false;
            }
        }
        catch
        {
            error = "Job path could not be resolved.";
            return false;
        }

        return true;
    }

    public static bool TryValidateMarker(
        string jobPath,
        string markerFileName,
        string expectedJobId,
        SftpJobType expectedJobType,
        out string? error)
    {
        error = null;
        var markerPath = Path.Combine(jobPath, markerFileName);
        if (!File.Exists(markerPath))
        {
            error = "Job directory marker is missing.";
            return false;
        }

        try
        {
            var json = File.ReadAllText(markerPath);
            using var document = System.Text.Json.JsonDocument.Parse(json);
            var root = document.RootElement;

            if (!root.TryGetProperty("jobId", out var jobIdElement)
                || !string.Equals(jobIdElement.GetString(), expectedJobId, StringComparison.OrdinalIgnoreCase))
            {
                error = "Job directory marker does not match the requested job.";
                return false;
            }

            if (!root.TryGetProperty("jobType", out var jobTypeElement))
            {
                error = "Job directory marker does not match the requested job type.";
                return false;
            }

            var markerJobType = jobTypeElement.ValueKind switch
            {
                JsonValueKind.String => jobTypeElement.GetString(),
                JsonValueKind.Number when jobTypeElement.TryGetInt32(out var numericJobType) =>
                    Enum.IsDefined(typeof(SftpJobType), numericJobType)
                        ? ((SftpJobType)numericJobType).ToString()
                        : null,
                _ => null
            };

            if (!string.Equals(markerJobType, expectedJobType.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                error = "Job directory marker does not match the requested job type.";
                return false;
            }
        }
        catch
        {
            error = "Job directory marker is invalid.";
            return false;
        }

        return true;
    }

    public static string BuildChrootPath(string chrootBasePath, string username) =>
        Path.Combine(chrootBasePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), username);

    public static string BuildFilesMountPath(string chrootPath) =>
        Path.Combine(chrootPath, "files");
}
