using ExcPanel.TransferAgent.Models;

namespace ExcPanel.TransferAgent.Contracts;

public interface ICommandRunner
{
    Task<CommandResult> RunAsync(
        string command,
        string args,
        int timeoutSeconds = 120,
        bool privileged = false,
        CancellationToken cancellationToken = default);
}
