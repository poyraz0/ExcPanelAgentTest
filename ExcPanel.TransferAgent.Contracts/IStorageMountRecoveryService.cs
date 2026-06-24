namespace ExcPanel.TransferAgent.Contracts;

public interface IStorageMountRecoveryService
{
    Task<bool> IsConfiguredAsync(string mountPath, CancellationToken cancellationToken = default);

    Task<bool> EnsureMountedAsync(string mountPath, CancellationToken cancellationToken = default);
}
