using ExcPanel.TransferAgent.Common;
using ExcPanel.TransferAgent.Contracts;
using ExcPanel.TransferAgent.Models;
using ExcPanel.TransferAgent.Options;
using Microsoft.Extensions.Options;

namespace ExcPanel.TransferAgent.Services.Storage;

public class StorageService : IStorageService
{
    private readonly TransferAgentOptions _options;

    public StorageService(IOptions<TransferAgentOptions> options)
    {
        _options = options.Value;
    }

    public Task<StorageStatusResponse> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var rootPath = _options.StorageRootPath;
        var exists = Directory.Exists(rootPath);

        var response = new StorageStatusResponse
        {
            RootPath = rootPath,
            Exists = exists
        };

        if (!exists)
        {
            return Task.FromResult(response);
        }

        var driveInfo = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(rootPath))!);

        if (!driveInfo.IsReady)
        {
            return Task.FromResult(response);
        }

        var totalBytes = driveInfo.TotalSize;
        var freeBytes = driveInfo.AvailableFreeSpace;
        var usedBytes = totalBytes - freeBytes;

        response.TotalBytes = totalBytes;
        response.FreeBytes = freeBytes;
        response.UsedBytes = usedBytes;
        response.TotalGb = ByteSizeHelper.ToGigabytes(totalBytes);
        response.FreeGb = ByteSizeHelper.ToGigabytes(freeBytes);
        response.UsedGb = ByteSizeHelper.ToGigabytes(usedBytes);

        return Task.FromResult(response);
    }
}
