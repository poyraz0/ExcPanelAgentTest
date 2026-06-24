using System.Text.Json;
using ExcPanel.TransferAgent.Contracts;
using ExcPanel.TransferAgent.Options;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;

namespace ExcPanel.TransferAgent.Services.Storage;

public class StorageSettingsWriter : IStorageSettingsWriter
{
    private const string StorageSettingsFileName = "appsettings.Storage.json";
    private readonly IWebHostEnvironment _environment;
    private readonly TransferAgentOptions _options;

    public StorageSettingsWriter(
        IWebHostEnvironment environment,
        IOptions<TransferAgentOptions> options)
    {
        _environment = environment;
        _options = options.Value;
    }

    public async Task WriteStorageRootPathAsync(string mountPath, CancellationToken cancellationToken = default)
    {
        var settings = new
        {
            Storage = new
            {
                RootPath = mountPath
            }
        };

        var settingsDirectory = ResolveSettingsDirectory();
        Directory.CreateDirectory(settingsDirectory);

        var filePath = Path.Combine(settingsDirectory, StorageSettingsFileName);
        var tempPath = $"{filePath}.{Guid.NewGuid():N}.tmp";
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }) +
                   Environment.NewLine;

        await File.WriteAllTextAsync(tempPath, json, cancellationToken);
        File.Move(tempPath, filePath, overwrite: true);
    }

    private string ResolveSettingsDirectory() =>
        string.IsNullOrWhiteSpace(_options.StateDirectory)
            ? _environment.ContentRootPath
            : _options.StateDirectory;
}
