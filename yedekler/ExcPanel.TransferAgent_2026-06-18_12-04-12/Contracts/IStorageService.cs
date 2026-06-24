using ExcPanel.TransferAgent.Models;

namespace ExcPanel.TransferAgent.Contracts;

public interface IStorageService
{
    Task<StorageStatusResponse> GetStatusAsync(CancellationToken cancellationToken = default);
}
