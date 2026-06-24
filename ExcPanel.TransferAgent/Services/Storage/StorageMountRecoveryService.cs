using System.Text.Json;
using ExcPanel.TransferAgent.Contracts;
using ExcPanel.TransferAgent.Options;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;

namespace ExcPanel.TransferAgent.Services.Storage;

public class StorageMountRecoveryService : IStorageMountRecoveryService
{
    private const string StorageSettingsFileName = "appsettings.Storage.json";

    private readonly IStorageMountChecker _mountChecker;
    private readonly IPrivilegedHelperClient _privilegedHelperClient;
    private readonly TransferAgentOptions _options;
    private readonly IWebHostEnvironment _environment;

    public StorageMountRecoveryService(
        IStorageMountChecker mountChecker,
        IPrivilegedHelperClient privilegedHelperClient,
        IOptions<TransferAgentOptions> options,
        IWebHostEnvironment environment)
    {
        _mountChecker = mountChecker;
        _privilegedHelperClient = privilegedHelperClient;
        _options = options.Value;
        _environment = environment;
    }

    public async Task<bool> IsConfiguredAsync(string mountPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(mountPath))
        {
            return false;
        }

        if (await FstabContainsMountPathAsync(mountPath, cancellationToken))
        {
            return true;
        }

        var settingsPath = ResolveStorageSettingsPath();
        return File.Exists(settingsPath);
    }

    public async Task<bool> EnsureMountedAsync(string mountPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(mountPath))
        {
            return false;
        }

        if (await _mountChecker.IsMountedAsync(mountPath, cancellationToken))
        {
            return true;
        }

        if (!await IsConfiguredAsync(mountPath, cancellationToken))
        {
            return false;
        }

        var response = await _privilegedHelperClient.InvokeAsync(new PrivilegedHelperRequest
        {
            RequestId = Guid.NewGuid().ToString("D"),
            Action = PrivilegedHelperActions.StorageRemount,
            Payload = JsonSerializer.SerializeToElement(
                new StorageRemountPayload { MountPath = mountPath },
                PrivilegedHelperJson.SerializerOptions)
        }, cancellationToken);

        if (!response.Success)
        {
            return false;
        }

        return await _mountChecker.IsMountedAsync(mountPath, cancellationToken);
    }

    private string ResolveStorageSettingsPath()
    {
        var stateDirectory = string.IsNullOrWhiteSpace(_options.StateDirectory)
            ? _environment.ContentRootPath
            : _options.StateDirectory;

        return Path.Combine(stateDirectory, StorageSettingsFileName);
    }

    private static async Task<bool> FstabContainsMountPathAsync(string mountPath, CancellationToken cancellationToken)
    {
        if (!File.Exists("/etc/fstab"))
        {
            return false;
        }

        var fstabContent = await File.ReadAllTextAsync("/etc/fstab", cancellationToken);
        return fstabContent
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(line => !line.StartsWith('#') && line.Contains(mountPath, StringComparison.Ordinal));
    }
}
