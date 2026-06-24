using System.Runtime.InteropServices;
using System.Text.Json;
using ExcPanel.TransferAgent.Contracts;
using ExcPanel.TransferAgent.Contracts.Validation;
using ExcPanel.TransferAgent.Models;
using ExcPanel.TransferAgent.Options;
using Microsoft.Extensions.Options;

namespace ExcPanel.TransferAgent.Services.Storage;

public class StorageConfigureService : IStorageConfigureService
{
    private readonly TransferAgentOptions _options;
    private readonly IStorageDiskDiscoveryService _diskDiscoveryService;
    private readonly IPrivilegedHelperClient _privilegedHelperClient;
    private readonly IConfirmationTokenService _confirmationTokenService;
    private readonly IStorageSettingsWriter _storageSettingsWriter;

    public StorageConfigureService(
        IOptions<TransferAgentOptions> options,
        IStorageDiskDiscoveryService diskDiscoveryService,
        IPrivilegedHelperClient privilegedHelperClient,
        IConfirmationTokenService confirmationTokenService,
        IStorageSettingsWriter storageSettingsWriter)
    {
        _options = options.Value;
        _diskDiscoveryService = diskDiscoveryService;
        _privilegedHelperClient = privilegedHelperClient;
        _confirmationTokenService = confirmationTokenService;
        _storageSettingsWriter = storageSettingsWriter;
    }

    public async Task<StorageConfigureDryRunResponse> DryRunAsync(
        StorageConfigureDryRunRequest request,
        CancellationToken cancellationToken = default)
    {
        var evaluation = await EvaluateAsync(request, cancellationToken);

        string? confirmationCode = null;
        if (evaluation.Valid && evaluation.Disk is not null)
        {
            var identity = StorageConfigureValidation.BuildConfirmationIdentity(evaluation.Disk, evaluation.TargetMountPath);
            confirmationCode = _confirmationTokenService.IssueToken(identity);
        }

        return new StorageConfigureDryRunResponse
        {
            Supported = evaluation.Supported,
            Valid = evaluation.Valid,
            Disk = evaluation.Disk,
            TargetMountPath = evaluation.TargetMountPath,
            ConfirmationCode = confirmationCode,
            PlannedActions = evaluation.PlannedActions,
            ValidationErrors = evaluation.ValidationErrors
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

        var evaluation = await EvaluateAsync(new StorageConfigureDryRunRequest
        {
            DiskPath = request.DiskPath,
            MountPath = request.MountPath
        }, cancellationToken);

        if (!evaluation.Supported)
        {
            return new ConfigureStorageOperationResult
            {
                Status = ConfigureStorageStatus.NotSupported,
                Message = evaluation.ValidationErrors.FirstOrDefault() ?? "Disk configuration is not supported on this platform."
            };
        }

        validationErrors.AddRange(evaluation.ValidationErrors);

        if (!string.Equals(request.MountPath.Trim(), evaluation.TargetMountPath, StringComparison.Ordinal))
        {
            validationErrors.Add("mountPath does not match the dry-run targetMountPath.");
        }

        if (validationErrors.Count > 0 || evaluation.Disk is null || !evaluation.Valid)
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

        var identity = StorageConfigureValidation.BuildConfirmationIdentity(evaluation.Disk, evaluation.TargetMountPath);
        if (!_confirmationTokenService.TryConsumeToken(request.ConfirmationCode, identity, out var tokenError))
        {
            return new ConfigureStorageOperationResult
            {
                Status = ConfigureStorageStatus.ValidationFailed,
                Message = "Storage configuration validation failed.",
                Failure = new ConfigureStorageFailureResponse
                {
                    ValidationErrors = [tokenError ?? "confirmationCode is invalid."]
                }
            };
        }

        var helperRequest = new PrivilegedHelperRequest
        {
            RequestId = Guid.NewGuid().ToString("D"),
            Action = PrivilegedHelperActions.StorageConfigure,
            Payload = JsonSerializer.SerializeToElement(new StorageConfigurePayload
            {
                DiskPath = evaluation.Disk.Path,
                ExpectedSerial = identity.DiskSerialOrWwn,
                ExpectedSizeBytes = evaluation.Disk.SizeBytes,
                MountPath = evaluation.TargetMountPath,
                FileSystem = request.FileSystem.Trim().ToLowerInvariant(),
                OwnerUser = _options.StorageOwnerUser,
                OwnerGroup = _options.StorageOwnerGroup,
                DirectoryMode = NormalizeDirectoryMode(_options.StorageDirectoryMode)
            })
        };

        var helperResponse = await _privilegedHelperClient.InvokeAsync(helperRequest, cancellationToken);
        if (!helperResponse.Success)
        {
            return MapHelperFailure(helperResponse);
        }

        var resultData = helperResponse.Data.Deserialize<StorageConfigureResultData>(PrivilegedHelperJson.SerializerOptions);
        if (resultData is null || string.IsNullOrWhiteSpace(resultData.MountPath))
        {
            return new ConfigureStorageOperationResult
            {
                Status = ConfigureStorageStatus.CommandFailed,
                Message = "Privileged helper returned an invalid success payload.",
                Failure = new ConfigureStorageFailureResponse
                {
                    ValidationErrors = ["Privileged helper returned an invalid success payload."]
                }
            };
        }

        try
        {
            await _storageSettingsWriter.WriteStorageRootPathAsync(resultData.MountPath, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ConfigureStorageOperationResult
            {
                Status = ConfigureStorageStatus.CommandFailed,
                Message = "Storage was configured on disk but settings could not be persisted.",
                Failure = new ConfigureStorageFailureResponse
                {
                    ValidationErrors = [$"Failed to write storage settings: {ex.Message}"]
                }
            };
        }

        return new ConfigureStorageOperationResult
        {
            Status = ConfigureStorageStatus.Success,
            Success = new ConfigureStorageResponse
            {
                Configured = true,
                DiskPath = resultData.DiskPath,
                PartitionPath = resultData.PartitionPath,
                MountPath = resultData.MountPath,
                FileSystem = resultData.FileSystem,
                Uuid = resultData.Uuid,
                CreatedDirectories = resultData.CreatedDirectories,
                FstabUpdated = resultData.FstabUpdated,
                Mounted = resultData.Mounted
            }
        };
    }

    private async Task<StorageConfigureEvaluation> EvaluateAsync(
        StorageConfigureDryRunRequest request,
        CancellationToken cancellationToken)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return new StorageConfigureEvaluation
            {
                Supported = false,
                Valid = false,
                TargetMountPath = ResolveTargetMountPath(request),
                ValidationErrors = ["Disk configuration is not implemented for this platform."]
            };
        }

        var targetMountPath = ResolveTargetMountPath(request);
        var validationErrors = new List<string>();
        StorageDiskInfo? selectedDisk = null;

        if (string.IsNullOrWhiteSpace(request.DiskPath))
        {
            validationErrors.Add("diskPath is required.");
        }
        else
        {
            selectedDisk = await FindDiskAsync(request.DiskPath, validationErrors, cancellationToken);
        }

        var isResume = selectedDisk is not null &&
            StorageConfigureValidation.IsStorageResumeScenario(selectedDisk, request.DiskPath.Trim(), targetMountPath);

        validationErrors.AddRange(StorageConfigureValidation.ValidateMountPath(targetMountPath, isResume));

        if (!string.IsNullOrWhiteSpace(request.DiskPath) && selectedDisk is not null)
        {
            validationErrors.AddRange(StorageConfigureValidation.ValidateDiskSelection(
                selectedDisk,
                request.DiskPath.Trim(),
                targetMountPath));
        }

        var valid = validationErrors.Count == 0 && selectedDisk is not null;
        var plannedActions = valid
            ? BuildPlannedActions(selectedDisk!, targetMountPath)
            : Array.Empty<string>();

        return new StorageConfigureEvaluation
        {
            Supported = true,
            Valid = valid,
            Disk = selectedDisk,
            TargetMountPath = targetMountPath,
            PlannedActions = plannedActions,
            ValidationErrors = validationErrors
        };
    }

    private sealed class StorageConfigureEvaluation
    {
        public bool Supported { get; init; }
        public bool Valid { get; init; }
        public StorageDiskInfo? Disk { get; init; }
        public string TargetMountPath { get; init; } = string.Empty;
        public IReadOnlyList<string> PlannedActions { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> ValidationErrors { get; init; } = Array.Empty<string>();
    }

    private static ConfigureStorageOperationResult MapHelperFailure(PrivilegedHelperResponse helperResponse)
    {
        var isHelperInfrastructureFailure =
            helperResponse.ErrorCode is "HELPER_NOT_FOUND" or "HELPER_NOT_AUTHORIZED" or "HELPER_START_FAILED";

        if (isHelperInfrastructureFailure)
        {
            return new ConfigureStorageOperationResult
            {
                Status = ConfigureStorageStatus.ValidationFailed,
                Message = helperResponse.Message,
                Failure = new ConfigureStorageFailureResponse
                {
                    ValidationErrors = [helperResponse.Message]
                }
            };
        }

        var validationErrors = new List<string> { helperResponse.Message };
        CommandResult? failedCommand = null;

        if (helperResponse.FailedCommandExitCode.HasValue)
        {
            failedCommand = new CommandResult
            {
                Command = helperResponse.FailedStep ?? "privileged-helper",
                Args = string.Empty,
                ExitCode = helperResponse.FailedCommandExitCode.Value,
                Stderr = helperResponse.FailedCommandStderr ?? string.Empty
            };
        }

        return new ConfigureStorageOperationResult
        {
            Status = ConfigureStorageStatus.CommandFailed,
            Message = helperResponse.Message,
            Failure = new ConfigureStorageFailureResponse
            {
                FailedStep = helperResponse.FailedStep,
                ValidationErrors = validationErrors,
                FailedCommand = failedCommand
            }
        };
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

    private string ResolveTargetMountPath(StorageConfigureDryRunRequest request)
    {
        var requested = request.ResolveMountPath();
        return string.IsNullOrWhiteSpace(requested)
            ? _options.StorageRootPath
            : requested;
    }

    private static string NormalizeDirectoryMode(string directoryMode) =>
        directoryMode.StartsWith('0') ? directoryMode : $"0{directoryMode}";

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
