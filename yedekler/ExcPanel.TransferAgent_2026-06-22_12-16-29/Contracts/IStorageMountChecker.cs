namespace ExcPanel.TransferAgent.Contracts;

public interface IStorageMountChecker
{
    Task<bool> IsMountedAsync(string path, CancellationToken cancellationToken = default);
}
