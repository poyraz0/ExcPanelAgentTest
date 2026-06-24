using ExcPanel.TransferAgent.Models;

namespace ExcPanel.TransferAgent.Contracts;

public interface ISafeProcessRunner
{
    Task<CommandResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        int timeoutSeconds = 120,
        CancellationToken cancellationToken = default);
}
