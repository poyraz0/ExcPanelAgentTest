using ExcPanel.TransferAgent.Models;

namespace ExcPanel.TransferAgent.Contracts;

public interface IStorageConfigureService
{
    Task<StorageConfigureDryRunResponse> DryRunAsync(
        StorageConfigureDryRunRequest request,
        CancellationToken cancellationToken = default);

    Task<ConfigureStorageOperationResult> ConfigureAsync(
        ConfigureStorageRequest request,
        CancellationToken cancellationToken = default);
}
