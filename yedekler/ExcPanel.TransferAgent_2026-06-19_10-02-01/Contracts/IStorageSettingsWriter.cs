namespace ExcPanel.TransferAgent.Contracts;

public interface IStorageSettingsWriter
{
    Task WriteStorageRootPathAsync(string mountPath, CancellationToken cancellationToken = default);
}
