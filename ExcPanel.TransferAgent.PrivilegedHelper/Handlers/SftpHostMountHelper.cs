using ExcPanel.TransferAgent.Contracts;
using ExcPanel.TransferAgent.PrivilegedHelper.Commands;

namespace ExcPanel.TransferAgent.PrivilegedHelper.Handlers;

internal static class SftpHostMountHelper
{
    internal const string HostMountInfoPath = "/proc/1/mountinfo";

    public static Task<CommandExecutionResult> RunInHostMountNamespaceAsync(
        IPrivilegedCommandRunner commandRunner,
        string executableKey,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken) =>
        RunInHostMountNamespaceAsync(
            commandRunner,
            executableKey,
            arguments,
            standardInput: null,
            cancellationToken);

    public static async Task<CommandExecutionResult> RunInHostMountNamespaceAsync(
        IPrivilegedCommandRunner commandRunner,
        string executableKey,
        IReadOnlyList<string> arguments,
        string? standardInput,
        CancellationToken cancellationToken)
    {
        if (commandRunner is LinuxPrivilegedCommandRunner linuxRunner
            && linuxRunner.ResolvedExecutables.TryGetValue(executableKey, out var executablePath))
        {
            var nsenterArguments = new List<string>
            {
                "-t",
                "1",
                "-m",
                "--",
                executablePath
            };
            nsenterArguments.AddRange(arguments);

            return await commandRunner.RunAsync(
                "nsenter",
                nsenterArguments,
                standardInput: standardInput,
                cancellationToken: cancellationToken);
        }

        return await commandRunner.RunAsync(
            executableKey,
            arguments,
            standardInput: standardInput,
            cancellationToken: cancellationToken);
    }

    public static Task<CommandExecutionResult> EnsureSharedMountPropagationAsync(
        IPrivilegedCommandRunner commandRunner,
        string chrootBasePath,
        CancellationToken cancellationToken) =>
        RunInHostMountNamespaceAsync(
            commandRunner,
            "mount",
            ["--make-rshared", chrootBasePath],
            cancellationToken);
}
