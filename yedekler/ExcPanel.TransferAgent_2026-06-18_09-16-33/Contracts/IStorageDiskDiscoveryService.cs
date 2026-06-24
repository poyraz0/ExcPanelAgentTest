using ExcPanel.TransferAgent.Models;

namespace ExcPanel.TransferAgent.Contracts;

public interface IStorageDiskDiscoveryService
{
    Task<StorageDiskDiscoveryResponse> DiscoverDisksAsync(CancellationToken cancellationToken = default);
}
