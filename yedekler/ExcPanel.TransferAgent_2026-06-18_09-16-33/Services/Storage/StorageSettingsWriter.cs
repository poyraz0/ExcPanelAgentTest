using System.Text.Json;
using ExcPanel.TransferAgent.Contracts;
using Microsoft.AspNetCore.Hosting;

namespace ExcPanel.TransferAgent.Services.Storage;

public class StorageSettingsWriter : IStorageSettingsWriter
{
    private const string StorageSettingsFileName = "appsettings.Storage.json";
    private readonly IWebHostEnvironment _environment;

    public StorageSettingsWriter(IWebHostEnvironment environment)
    {
        _environment = environment;
    }

    public async Task WriteStorageRootPathAsync(string mountPath, CancellationToken cancellationToken = default)
    {
        var settings = new
        {
            TransferAgent = new
            {
                StorageRootPath = mountPath
            }
        };

        var filePath = Path.Combine(_environment.ContentRootPath, StorageSettingsFileName);
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(filePath, json + Environment.NewLine, cancellationToken);
    }
}
