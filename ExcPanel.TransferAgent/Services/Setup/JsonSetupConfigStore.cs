using System.Text.Json;
using ExcPanel.TransferAgent.Contracts;
using ExcPanel.TransferAgent.Models.Setup;
using ExcPanel.TransferAgent.Options;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace ExcPanel.TransferAgent.Services.Setup;

public class JsonSetupConfigStore : ISetupConfigStore
{
    private const string ConfigFileName = "setup-config.json";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly string _configFilePath;

    public JsonSetupConfigStore(IWebHostEnvironment environment, IOptions<TransferAgentOptions> options, IHostEnvironment hostEnvironment)
    {
        var stateDirectory = JsonSetupStateStore.ResolveStateDirectory(environment, options.Value, hostEnvironment);
        Directory.CreateDirectory(stateDirectory);
        _configFilePath = Path.Combine(stateDirectory, ConfigFileName);
    }

    public async Task<SetupConfigDocument> GetAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_configFilePath))
            {
                return new SetupConfigDocument();
            }

            await using var stream = File.OpenRead(_configFilePath);
            return await JsonSerializer.DeserializeAsync<SetupConfigDocument>(stream, JsonOptions, cancellationToken)
                ?? new SetupConfigDocument();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveAsync(SetupConfigDocument config, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            config.UpdatedAtUtc = DateTime.UtcNow;
            var tempPath = $"{_configFilePath}.{Guid.NewGuid():N}.tmp";
            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, config, JsonOptions, cancellationToken);
            }

            File.Move(tempPath, _configFilePath, overwrite: true);
        }
        finally
        {
            _lock.Release();
        }
    }
}
