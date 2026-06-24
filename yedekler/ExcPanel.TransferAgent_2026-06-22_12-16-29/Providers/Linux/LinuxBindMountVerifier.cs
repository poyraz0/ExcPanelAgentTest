namespace ExcPanel.TransferAgent.Providers.Linux;

internal static class LinuxBindMountVerifier
{
    public static bool IsBindMountActive(string filesMountPath, string expectedJobPath)
    {
        if (!OperatingSystem.IsLinux())
        {
            return false;
        }

        var normalizedMountPoint = NormalizePath(filesMountPath);
        var normalizedJobPath = NormalizePath(expectedJobPath);

        foreach (var line in ReadMountInfoLines())
        {
            if (!TryParseMountInfoLine(line, out var root, out var mountPoint, out var source))
            {
                continue;
            }

            if (!string.Equals(mountPoint, normalizedMountPoint, StringComparison.Ordinal))
            {
                continue;
            }

            var resolvedSource = ResolveJobPathFromBindRoot(root, source);
            return resolvedSource is not null
                && string.Equals(resolvedSource, normalizedJobPath, StringComparison.Ordinal);
        }

        return false;
    }

    private static string? ResolveJobPathFromBindRoot(string root, string sourceDevice)
    {
        var normalizedRoot = root.TrimStart('/');
        foreach (var line in ReadMountInfoLines())
        {
            if (!TryParseMountInfoLine(line, out _, out var mountPoint, out var source))
            {
                continue;
            }

            if (!string.Equals(source, sourceDevice, StringComparison.Ordinal))
            {
                continue;
            }

            return NormalizePath(Path.Combine(mountPoint, normalizedRoot));
        }

        return null;
    }

    private static IEnumerable<string> ReadMountInfoLines()
    {
        if (!File.Exists("/proc/1/mountinfo"))
        {
            yield break;
        }

        foreach (var line in File.ReadLines("/proc/1/mountinfo"))
        {
            yield return line;
        }
    }

    private static bool TryParseMountInfoLine(
        string line,
        out string root,
        out string mountPoint,
        out string source)
    {
        root = string.Empty;
        mountPoint = string.Empty;
        source = string.Empty;

        var separatorIndex = line.IndexOf(" - ", StringComparison.Ordinal);
        if (separatorIndex < 0)
        {
            return false;
        }

        var prefixFields = line[..separatorIndex].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (prefixFields.Length < 5)
        {
            return false;
        }

        var suffixFields = line[(separatorIndex + 3)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (suffixFields.Length < 2)
        {
            return false;
        }

        root = DecodeMountInfoPath(prefixFields[3]);
        mountPoint = DecodeMountInfoPath(prefixFields[4]);
        source = suffixFields[1];
        return true;
    }

    private static string NormalizePath(string path) =>
        Path.GetFullPath(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

    private static string DecodeMountInfoPath(string value) =>
        value.Replace("\\040", " ", StringComparison.Ordinal)
            .Replace("\\011", "\t", StringComparison.Ordinal);
}
