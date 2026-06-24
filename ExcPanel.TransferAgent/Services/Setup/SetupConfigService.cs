using ExcPanel.TransferAgent.Contracts;
using ExcPanel.TransferAgent.Models.Setup;
using ExcPanel.TransferAgent.Options;
using Microsoft.Extensions.Options;

namespace ExcPanel.TransferAgent.Services.Setup;

public class SetupConfigService : ISetupConfigService
{
    private readonly ISetupConfigStore _configStore;
    private readonly SetupOptions _setupOptions;

    public SetupConfigService(ISetupConfigStore configStore, IOptions<SetupOptions> setupOptions)
    {
        _configStore = configStore;
        _setupOptions = setupOptions.Value;
    }

    public async Task<SetupConfigDocument> GetConfigAsync(CancellationToken cancellationToken = default)
    {
        var config = await _configStore.GetAsync(cancellationToken);
        return MaskSensitiveFields(config);
    }

    public async Task<SetupConfigDocument> UpdateStorageAsync(SetupStorageConfig config, CancellationToken cancellationToken = default)
    {
        var document = await _configStore.GetAsync(cancellationToken);
        document.Storage = config;
        await _configStore.SaveAsync(document, cancellationToken);
        return MaskSensitiveFields(document);
    }

    public async Task<SetupConfigDocument> UpdateDomainAsync(SetupDomainConfig config, CancellationToken cancellationToken = default)
    {
        var document = await _configStore.GetAsync(cancellationToken);
        document.Domain = config;
        await _configStore.SaveAsync(document, cancellationToken);
        return MaskSensitiveFields(document);
    }

    public async Task<SetupConfigDocument> UpdateSambaAsync(SetupSambaConfig config, CancellationToken cancellationToken = default)
    {
        var document = await _configStore.GetAsync(cancellationToken);
        document.Samba = config;
        await _configStore.SaveAsync(document, cancellationToken);
        return MaskSensitiveFields(document);
    }

    public async Task<SetupConfigDocument> UpdateSftpAsync(SetupSftpConfig config, CancellationToken cancellationToken = default)
    {
        var document = await _configStore.GetAsync(cancellationToken);
        document.Sftp = config;
        await _configStore.SaveAsync(document, cancellationToken);
        return MaskSensitiveFields(document);
    }

    private static SetupConfigDocument MaskSensitiveFields(SetupConfigDocument config)
    {
        // Domain password is never stored; domain config has no password field.
        return config;
    }
}
