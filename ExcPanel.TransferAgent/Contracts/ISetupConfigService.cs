using ExcPanel.TransferAgent.Models.Setup;

namespace ExcPanel.TransferAgent.Contracts;

public interface ISetupConfigService
{
    Task<SetupConfigDocument> GetConfigAsync(CancellationToken cancellationToken = default);
    Task<SetupConfigDocument> UpdateStorageAsync(SetupStorageConfig config, CancellationToken cancellationToken = default);
    Task<SetupConfigDocument> UpdateDomainAsync(SetupDomainConfig config, CancellationToken cancellationToken = default);
    Task<SetupConfigDocument> UpdateSambaAsync(SetupSambaConfig config, CancellationToken cancellationToken = default);
    Task<SetupConfigDocument> UpdateSftpAsync(SetupSftpConfig config, CancellationToken cancellationToken = default);
}
