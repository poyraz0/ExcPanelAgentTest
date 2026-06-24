using ExcPanel.TransferAgent.Contracts;
using ExcPanel.TransferAgent.PrivilegedHelper.Commands;

namespace ExcPanel.TransferAgent.Tests.Fakes;

public sealed class FakePrivilegedCommandRunner : IPrivilegedCommandRunner
{
    private readonly Dictionary<string, Func<IReadOnlyList<string>, CommandExecutionResult>> _handlers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _executables;

    public FakePrivilegedCommandRunner()
    {
        _executables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["parted"] = "/usr/sbin/parted",
            ["partprobe"] = "/usr/sbin/partprobe",
            ["mkfs.ext4"] = "/usr/sbin/mkfs.ext4",
            ["blkid"] = "/usr/sbin/blkid",
            ["mount"] = "/usr/bin/mount",
            ["findmnt"] = "/usr/bin/findmnt",
            ["lsblk"] = "/usr/bin/lsblk",
            ["chown"] = "/usr/bin/chown",
            ["chmod"] = "/usr/bin/chmod",
            ["useradd"] = "/usr/sbin/useradd",
            ["usermod"] = "/usr/sbin/usermod",
            ["userdel"] = "/usr/sbin/userdel",
            ["groupadd"] = "/usr/sbin/groupadd",
            ["chpasswd"] = "/usr/sbin/chpasswd",
            ["passwd"] = "/usr/bin/passwd",
            ["sshd"] = "/usr/sbin/sshd",
            ["systemctl"] = "/usr/bin/systemctl",
            ["install"] = "/usr/bin/install",
            ["umount"] = "/usr/bin/umount",
            ["testparm"] = "/usr/bin/testparm",
            ["setfacl"] = "/usr/bin/setfacl",
            ["getent"] = "/usr/bin/getent",
            ["wbinfo"] = "/usr/bin/wbinfo",
            ["realm"] = "/usr/bin/realm",
            ["net"] = "/usr/bin/net",
            ["dpkg-query"] = "/usr/bin/dpkg-query"
        };
    }

    public IReadOnlyDictionary<string, string> ResolvedExecutables => _executables;

    public List<(string Key, IReadOnlyList<string> Arguments)> Invocations { get; } = [];

    public List<(string Key, string StandardInput)> StdinPayloads { get; } = [];

    public void SetHandler(string executableKey, Func<IReadOnlyList<string>, CommandExecutionResult> handler)
    {
        _handlers[executableKey] = handler;
    }

    public Task<CommandExecutionResult> RunAsync(
        string executableKey,
        IReadOnlyList<string> arguments,
        int timeoutSeconds = 120,
        string? standardInput = null,
        CancellationToken cancellationToken = default)
    {
        Invocations.Add((executableKey, arguments));

        if (standardInput is not null)
        {
            StdinPayloads.Add((executableKey, standardInput));
        }

        if (!_executables.ContainsKey(executableKey))
        {
            return Task.FromResult(new CommandExecutionResult
            {
                Executable = executableKey,
                Arguments = arguments,
                ExitCode = -1,
                Stderr = $"Executable '{executableKey}' is not in the allowlist."
            });
        }

        if (_handlers.TryGetValue(executableKey, out var handler))
        {
            return Task.FromResult(handler(arguments));
        }

        return Task.FromResult(new CommandExecutionResult
        {
            Executable = _executables.GetValueOrDefault(executableKey, executableKey),
            Arguments = arguments,
            ExitCode = 0
        });
    }
}
