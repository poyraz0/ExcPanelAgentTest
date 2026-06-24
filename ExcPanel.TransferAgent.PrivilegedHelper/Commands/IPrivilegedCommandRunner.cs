using ExcPanel.TransferAgent.Contracts;

namespace ExcPanel.TransferAgent.PrivilegedHelper.Commands;

public interface IPrivilegedCommandRunner
{
    IReadOnlyDictionary<string, string> ResolvedExecutables { get; }

    Task<CommandExecutionResult> RunAsync(
        string executableKey,
        IReadOnlyList<string> arguments,
        int timeoutSeconds = 120,
        string? standardInput = null,
        CancellationToken cancellationToken = default);
}
