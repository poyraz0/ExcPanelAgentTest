using System.Runtime.InteropServices;
using ExcPanel.TransferAgent.Contracts;
using ExcPanel.TransferAgent.Models;
using ExcPanel.TransferAgent.Options;
using Microsoft.Extensions.Options;

namespace ExcPanel.TransferAgent.Services.Storage;

public class StorageConfigureService : IStorageConfigureService
{
    private readonly TransferAgentOptions _options;
    private readonly IStorageDiskDiscoveryService _diskDiscoveryService;
    private readonly LinuxStorageConfigurer _linuxStorageConfigurer;

    public StorageConfigureService(
        IOptions<TransferAgentOptions> options,
        IStorageDiskDiscoveryService diskDiscoveryService,
        LinuxStorageConfigurer linuxStorageConfigurer)
    {
        _options = options.Value;
        _diskDiscoveryService = diskDiscoveryService;
        _linuxStorageConfigurer = linuxStorageConfigurer;
    }

    public async Task<StorageConfigureDryRunResponse> DryRunAsync(
        StorageConfigureDryRunRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return new StorageConfigureDryRunResponse
            {
                Supported = false,
                Valid = false,
                TargetMountPath = ResolveTargetMountPath(request.TargetMountPath),
                ValidationErrors = ["Disk configuration is not implemented for this platform."]
            };
        }

        var targetMountPath = ResolveTargetMountPath(request.TargetMountPath);
        var validationErrors = new List<string>();
        StorageDiskInfo? selectedDisk = null;

        validationErrors.AddRange(StorageConfigureValidation.ValidateMountPath(targetMountPath));

        if (string.IsNullOrWhiteSpace(request.DiskPath))
        {
            validationErrors.Add("diskPath is required.");
        }
        else
        {
            selectedDisk = await FindDiskAsync(request.DiskPath, validationErrors, cancellationToken);
            validationErrors.AddRange(StorageConfigureValidation.ValidateDiskSelection(selectedDisk, request.DiskPath.Trim()));
        }

        var valid = validationErrors.Count == 0 && selectedDisk is not null;
        var plannedActions = valid
            ? BuildPlannedActions(selectedDisk!, targetMountPath)
            : Array.Empty<string>();

        return new StorageConfigureDryRunResponse
        {
            Supported = true,
            Valid = valid,
            Disk = selectedDisk,
            TargetMountPath = targetMountPath,
            ConfirmationCode = valid ? StorageConfigureValidation.BuildConfirmationCode(selectedDisk!) : null,
            PlannedActions = plannedActions,
            ValidationErrors = validationErrors
        };
    }

    public async Task<ConfigureStorageOperationResult> ConfigureAsync(
        ConfigureStorageRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return new ConfigureStorageOperationResult
            {
                Status = ConfigureStorageStatus.NotSupported,
                Message = "Storage configuration is only supported on Linux."
            };
        }

        var validationErrors = new List<string>();
        validationErrors.AddRange(StorageConfigureValidation.ValidateFileSystem(request.FileSystem));

        if (string.IsNullOrWhiteSpace(request.ConfirmationCode))
        {
            validationErrors.Add("confirmationCode is required.");
        }

        var dryRun = await DryRunAsync(new StorageConfigureDryRunRequest
        {
            DiskPath = request.DiskPath,
            TargetMountPath = request.MountPath
        }, cancellationToken);

        if (!dryRun.Supported)
        {
            return new ConfigureStorageOperationResult
            {
                Status = ConfigureStorageStatus.NotSupported,
                Message = dryRun.ValidationErrors.FirstOrDefault() ?? "Disk configuration is not supported on this platform."
            };
        }

        validationErrors.AddRange(dryRun.ValidationErrors);

        if (!string.IsNullOrWhiteSpace(request.ConfirmationCode) && !string.IsNullOrWhiteSpace(dryRun.ConfirmationCode) &&
            !string.Equals(request.ConfirmationCode.Trim(), dryRun.ConfirmationCode, StringComparison.Ordinal))
        {
            validationErrors.Add($"confirmationCode does not match expected value '{dryRun.ConfirmationCode}'.");
        }

        if (validationErrors.Count > 0 || dryRun.Disk is null || !dryRun.Valid)
        {
            return new ConfigureStorageOperationResult
            {
                Status = ConfigureStorageStatus.ValidationFailed,
                Message = "Storage configuration validation failed.",
                Failure = new ConfigureStorageFailureResponse
                {
                    ValidationErrors = validationErrors.Distinct(StringComparer.Ordinal).ToList()
                }
            };
        }

        return await _linuxStorageConfigurer.ConfigureAsync(request, dryRun.Disk, cancellationToken);
    }

    private async Task<StorageDiskInfo?> FindDiskAsync(
        string diskPath,
        List<string> validationErrors,
        CancellationToken cancellationToken)
    {
        var discovery = await _diskDiscoveryService.DiscoverDisksAsync(cancellationToken);

        if (!discovery.Supported)
        {
            validationErrors.Add(discovery.Message ?? "Disk discovery is not supported on this platform.");
            return null;
        }

        if (!string.IsNullOrWhiteSpace(discovery.Message) && discovery.Disks.Count == 0)
        {
            validationErrors.Add(discovery.Message);
        }

        return discovery.Disks.FirstOrDefault(disk =>
            string.Equals(disk.Path, diskPath, StringComparison.Ordinal) ||
            string.Equals(disk.Name, diskPath.TrimStart('/'), StringComparison.Ordinal));
    }

    private string ResolveTargetMountPath(string? targetMountPath) =>
        string.IsNullOrWhiteSpace(targetMountPath)
            ? _options.StorageRootPath
            : targetMountPath.Trim();

    private static IReadOnlyList<string> BuildPlannedActions(StorageDiskInfo disk, string targetMountPath)
    {
        var partitionPath = StorageConfigureValidation.ResolvePartitionPath(disk.Path);

        return
        [
            $"Create GPT partition table on {disk.Path}",
            $"Create primary partition on {disk.Path}",
            $"Format partition {partitionPath} with ext4 filesystem",
            $"Create mount directory {targetMountPath}",
            $"Persist mount entry in /etc/fstab for {targetMountPath}",
            $"Mount formatted partition to {targetMountPath}",
            $"Create exports/imports/temp/logs directories under {targetMountPath}",
            $"Set ownership and permissions for transfer agent storage access",
            $"Write StorageRootPath override to appsettings.Storage.json"
        ];
    }
}
