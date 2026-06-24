using System.Diagnostics;
using ExcPanel.TransferAgent.Contracts;

namespace ExcPanel.TransferAgent.PrivilegedHelper.Commands;

public class LinuxPrivilegedCommandRunner : IPrivilegedCommandRunner
{
    private static readonly string[] ExecutableKeys =
    [
        "parted",
        "partprobe",
        "mkfs.ext4",
        "blkid",
        "mount",
        "umount",
        "findmnt",
        "nsenter",
        "lsblk",
        "chown",
        "chmod",
        "useradd",
        "usermod",
        "userdel",
        "groupadd",
        "chpasswd",
        "passwd",
        "sshd",
        "systemctl",
        "install",
        "testparm",
        "setfacl",
        "getent",
        "wbinfo",
        "realm",
        "net",
        "dpkg-query"
    ];

    private static readonly Dictionary<string, string[]> SearchPaths = new(StringComparer.Ordinal)
    {
        ["parted"] = ["/usr/sbin/parted", "/sbin/parted"],
        ["partprobe"] = ["/usr/sbin/partprobe", "/sbin/partprobe", "/usr/bin/partprobe", "/bin/partprobe"],
        ["mkfs.ext4"] = ["/usr/sbin/mkfs.ext4", "/sbin/mkfs.ext4"],
        ["blkid"] = ["/usr/sbin/blkid", "/sbin/blkid", "/usr/bin/blkid", "/bin/blkid"],
        ["mount"] = ["/usr/bin/mount", "/bin/mount"],
        ["findmnt"] = ["/usr/bin/findmnt", "/bin/findmnt"],
        ["umount"] = ["/usr/bin/umount", "/bin/umount"],
        ["nsenter"] = ["/usr/bin/nsenter", "/bin/nsenter"],
        ["lsblk"] = ["/usr/bin/lsblk", "/bin/lsblk"],
        ["chown"] = ["/usr/bin/chown", "/bin/chown"],
        ["chmod"] = ["/usr/bin/chmod", "/bin/chmod"],
        ["useradd"] = ["/usr/sbin/useradd", "/sbin/useradd"],
        ["usermod"] = ["/usr/sbin/usermod", "/sbin/usermod"],
        ["userdel"] = ["/usr/sbin/userdel", "/sbin/userdel"],
        ["groupadd"] = ["/usr/sbin/groupadd", "/sbin/groupadd"],
        ["chpasswd"] = ["/usr/sbin/chpasswd", "/sbin/chpasswd"],
        ["passwd"] = ["/usr/bin/passwd", "/bin/passwd"],
        ["sshd"] = ["/usr/sbin/sshd", "/sbin/sshd"],
        ["systemctl"] = ["/usr/bin/systemctl", "/bin/systemctl"],
        ["install"] = ["/usr/bin/install", "/bin/install"],
        ["testparm"] = ["/usr/bin/testparm", "/sbin/testparm"],
        ["setfacl"] = ["/usr/bin/setfacl", "/bin/setfacl"],
        ["getent"] = ["/usr/bin/getent", "/bin/getent"],
        ["wbinfo"] = ["/usr/bin/wbinfo", "/sbin/wbinfo"],
        ["realm"] = ["/usr/bin/realm", "/sbin/realm"],
        ["net"] = ["/usr/bin/net", "/sbin/net"],
        ["dpkg-query"] = ["/usr/bin/dpkg-query", "/bin/dpkg-query"]
    };

    private readonly Dictionary<string, string> _resolvedExecutables;

    public LinuxPrivilegedCommandRunner()
    {
        _resolvedExecutables = ResolveExecutables();
    }

    public IReadOnlyDictionary<string, string> ResolvedExecutables => _resolvedExecutables;

    public async Task<CommandExecutionResult> RunAsync(
        string executableKey,
        IReadOnlyList<string> arguments,
        int timeoutSeconds = 120,
        string? standardInput = null,
        CancellationToken cancellationToken = default)
    {
        if (!_resolvedExecutables.TryGetValue(executableKey, out var executablePath))
        {
            return new CommandExecutionResult
            {
                Executable = executableKey,
                Arguments = arguments,
                ExitCode = -1,
                Stderr = $"Executable '{executableKey}' is not in the allowlist."
            };
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (standardInput is not null)
        {
            startInfo.RedirectStandardInput = true;
        }

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        if (standardInput is not null)
        {
            await process.StandardInput.WriteAsync(standardInput);
            await process.StandardInput.FlushAsync();
            process.StandardInput.Close();
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        try
        {
            var outputTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            var errorTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);
            await process.WaitForExitAsync(timeoutCts.Token);

            return new CommandExecutionResult
            {
                Executable = executablePath,
                Arguments = arguments,
                ExitCode = process.ExitCode,
                Stdout = (await outputTask).Trim(),
                Stderr = (await errorTask).Trim()
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // Best effort.
            }

            return new CommandExecutionResult
            {
                Executable = executablePath,
                Arguments = arguments,
                ExitCode = -1,
                Stderr = $"Command timed out after {timeoutSeconds} seconds.",
                TimedOut = true
            };
        }
    }

    private static Dictionary<string, string> ResolveExecutables()
    {
        var resolved = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var key in ExecutableKeys)
        {
            if (!SearchPaths.TryGetValue(key, out var candidates))
            {
                throw new InvalidOperationException($"No search paths configured for '{key}'.");
            }

            var path = candidates.Select(ResolveRealExecutablePath).FirstOrDefault(p => p is not null);
            if (path is null)
            {
                throw new InvalidOperationException($"Required executable '{key}' was not found.");
            }

            resolved[key] = path;
        }

        return resolved;
    }

    internal static string? ResolveRealExecutablePath(string candidatePath)
    {
        if (!File.Exists(candidatePath))
        {
            return null;
        }

        try
        {
            var fileInfo = new FileInfo(candidatePath);
            if (!fileInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                return fileInfo.FullName;
            }

            var linkTarget = fileInfo.ResolveLinkTarget(returnFinalTarget: true);
            return linkTarget?.FullName;
        }
        catch
        {
            return null;
        }
    }
}
