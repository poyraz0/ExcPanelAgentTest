using System.Text.Json;
using ExcPanel.TransferAgent.Contracts;
using ExcPanel.TransferAgent.Contracts.Validation;
using ExcPanel.TransferAgent.PrivilegedHelper.Commands;

namespace ExcPanel.TransferAgent.PrivilegedHelper.Handlers;

public class StorageConfigureHandler
{
    private static readonly string[] StorageSubdirectories = ["exports", "imports", "temp", "logs"];
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IPrivilegedCommandRunner _commandRunner;
    private readonly DiskInspector _diskInspector;

    public StorageConfigureHandler(IPrivilegedCommandRunner commandRunner, DiskInspector diskInspector)
    {
        _commandRunner = commandRunner;
        _diskInspector = diskInspector;
    }

    public async Task<PrivilegedHelperResponse> HandleAsync(
        string requestId,
        JsonElement payloadElement,
        CancellationToken cancellationToken)
    {
        StorageConfigurePayload payload;
        try
        {
            payload = payloadElement.Deserialize<StorageConfigurePayload>(JsonOptions)
                ?? throw new JsonException("Payload is null.");
        }
        catch (JsonException)
        {
            return PrivilegedHelperResponse.Failure(
                requestId,
                PrivilegedHelperErrorCodes.InvalidArguments,
                "storage.configure payload is invalid.");
        }

        var completedSteps = new List<string>();
        var validationError = await ValidatePayloadAsync(requestId, payload, cancellationToken);
        if (validationError is not null)
        {
            return validationError;
        }

        var diskPath = payload.DiskPath.Trim();
        var mountPath = payload.MountPath.Trim();
        var partitionPath = StorageValidationHelpers.ResolvePartitionPath(diskPath);
        var fileSystem = payload.FileSystem.Trim().ToLowerInvariant();

        var idempotentResult = await TryCompleteExistingConfigurationAsync(
            requestId,
            payload,
            diskPath,
            mountPath,
            partitionPath,
            fileSystem,
            completedSteps,
            cancellationToken);
        if (idempotentResult is not null)
        {
            return idempotentResult;
        }

        string? uuid = null;
        var fstabUpdated = false;

        var steps = new (string Step, Func<Task<PrivilegedHelperResponse?>> Action)[]
        {
            ("CreateGptPartitionTable", () => RunStepAsync(requestId, "parted", [diskPath, "--script", "mklabel", "gpt"], completedSteps, "CreateGptPartitionTable", cancellationToken)),
            ("CreatePrimaryPartition", () => RunStepAsync(requestId, "parted", [diskPath, "--script", "mkpart", "primary", fileSystem, "0%", "100%"], completedSteps, "CreatePrimaryPartition", cancellationToken)),
            ("ProbePartitionTable", () => RunStepAsync(requestId, "partprobe", [diskPath], completedSteps, "ProbePartitionTable", cancellationToken)),
            ("WaitForPartition", () => WaitForPartitionAsync(requestId, partitionPath, completedSteps, cancellationToken)),
            ("FormatPartition", () => RunStepAsync(requestId, "mkfs.ext4", ["-F", partitionPath], completedSteps, "FormatPartition", cancellationToken)),
            ("CreateMountDirectory", () => CreateDirectoryStepAsync(mountPath, completedSteps, cancellationToken)),
            ("ReadPartitionUuid", () => Task.FromResult<PrivilegedHelperResponse?>(null)),
            ("BackupFstab", () => BackupFstabStepAsync(completedSteps, cancellationToken)),
            ("UpdateFstab", () => UpdateFstabStepAsync(requestId, partitionPath, mountPath, fileSystem, completedSteps, cancellationToken)),
            ("MountStoragePath", () => MountStepAsync(requestId, mountPath, partitionPath, fileSystem, uuid, completedSteps, cancellationToken)),
            ("VerifyMount", () => VerifyMountStepAsync(requestId, mountPath, completedSteps, cancellationToken)),
            ("CreateSubdirectories", () => CreateSubdirectoriesStepAsync(mountPath, completedSteps, cancellationToken)),
            ("SetOwnership", () => SetOwnershipStepAsync(requestId, mountPath, payload.OwnerUser, payload.OwnerGroup, completedSteps, cancellationToken)),
            ("SetPermissions", () => SetPermissionsStepAsync(requestId, mountPath, payload.DirectoryMode, completedSteps, cancellationToken))
        };

        foreach (var (step, action) in steps)
        {
            if (step == "ReadPartitionUuid")
            {
                var uuidResult = await _commandRunner.RunAsync(
                    "blkid",
                    ["-s", "UUID", "-o", "value", partitionPath],
                    cancellationToken: cancellationToken);

                if (uuidResult.ExitCode != 0 || string.IsNullOrWhiteSpace(uuidResult.Stdout))
                {
                    return FailureFromCommand(requestId, "ReadPartitionUuid", uuidResult, completedSteps);
                }

                uuid = uuidResult.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries).First().Trim();
                completedSteps.Add("ReadPartitionUuid");
                continue;
            }

            if (step == "UpdateFstab")
            {
                var updateResult = await action();
                if (updateResult is not null)
                {
                    return updateResult;
                }

                fstabUpdated = true;
                continue;
            }

            var failure = await action();
            if (failure is not null)
            {
                return failure;
            }
        }

        var createdDirectories = StorageSubdirectories
            .Select(name => Path.Combine(mountPath, name))
            .ToList();

        var data = JsonSerializer.SerializeToElement(new StorageConfigureResultData
        {
            DiskPath = diskPath,
            PartitionPath = partitionPath,
            MountPath = mountPath,
            FileSystem = fileSystem,
            Uuid = uuid ?? string.Empty,
            CreatedDirectories = createdDirectories,
            FstabUpdated = fstabUpdated,
            Mounted = true
        }, JsonOptions);

        return PrivilegedHelperResponse.Ok(
            requestId,
            "Storage configured successfully.",
            completedSteps,
            data);
    }

    private async Task<PrivilegedHelperResponse?> ValidatePayloadAsync(
        string requestId,
        StorageConfigurePayload payload,
        CancellationToken cancellationToken)
    {
        var diskPath = payload.DiskPath?.Trim() ?? string.Empty;

        if (StorageValidationHelpers.IsPartitionPath(diskPath))
        {
            return PrivilegedHelperResponse.Failure(
                requestId,
                PrivilegedHelperErrorCodes.PartitionPathNotAllowed,
                $"diskPath '{diskPath}' appears to be a partition path. Only whole disks are accepted.");
        }

        if (!StorageValidationHelpers.IsValidWholeDiskPath(diskPath))
        {
            return PrivilegedHelperResponse.Failure(
                requestId,
                PrivilegedHelperErrorCodes.InvalidDiskPath,
                $"diskPath '{diskPath}' is not a supported whole-disk path.");
        }

        var partitionPath = StorageValidationHelpers.ResolvePartitionPath(diskPath);
        var partitionExists = File.Exists(partitionPath);

        var mountErrors = StorageValidationHelpers.ValidateMountPath(
            payload.MountPath,
            partitionExists || Directory.Exists(payload.MountPath.Trim()));
        if (mountErrors.Count > 0)
        {
            return PrivilegedHelperResponse.Failure(
                requestId,
                PrivilegedHelperErrorCodes.InvalidMountPath,
                mountErrors[0]);
        }

        var fileSystemErrors = StorageValidationHelpers.ValidateFileSystem(payload.FileSystem);
        if (fileSystemErrors.Count > 0)
        {
            return PrivilegedHelperResponse.Failure(
                requestId,
                PrivilegedHelperErrorCodes.InvalidFileSystem,
                fileSystemErrors[0]);
        }

        if (!UnixIdentity.Exists(payload.OwnerUser))
        {
            return PrivilegedHelperResponse.Failure(
                requestId,
                PrivilegedHelperErrorCodes.InvalidOwner,
                $"ownerUser '{payload.OwnerUser}' does not exist.");
        }

        if (!UnixIdentity.ExistsGroup(payload.OwnerGroup))
        {
            return PrivilegedHelperResponse.Failure(
                requestId,
                PrivilegedHelperErrorCodes.InvalidOwner,
                $"ownerGroup '{payload.OwnerGroup}' does not exist.");
        }

        var diskInfo = await _diskInspector.GetDiskAsync(diskPath, cancellationToken);
        if (diskInfo is null)
        {
            return PrivilegedHelperResponse.Failure(
                requestId,
                PrivilegedHelperErrorCodes.InvalidDiskPath,
                $"Disk '{diskPath}' was not found.");
        }

        if (diskInfo.IsSystemDisk)
        {
            return PrivilegedHelperResponse.Failure(
                requestId,
                PrivilegedHelperErrorCodes.SystemDisk,
                "Selected disk is a system disk and cannot be configured.");
        }

        var mountPath = payload.MountPath.Trim();
        if (!string.IsNullOrWhiteSpace(diskInfo.MountPoint) &&
            !string.Equals(diskInfo.MountPoint, mountPath, StringComparison.Ordinal))
        {
            return PrivilegedHelperResponse.Failure(
                requestId,
                PrivilegedHelperErrorCodes.DiskMounted,
                $"Disk is mounted at '{diskInfo.MountPoint}' and cannot be configured for '{mountPath}'.");
        }

        if (!partitionExists)
        {
            if (diskInfo.IsMounted)
            {
                return PrivilegedHelperResponse.Failure(
                    requestId,
                    PrivilegedHelperErrorCodes.DiskMounted,
                    "Selected disk is mounted and is not suitable for configuration.");
            }

            if (diskInfo.HasPartitions)
            {
                return PrivilegedHelperResponse.Failure(
                    requestId,
                    PrivilegedHelperErrorCodes.DiskHasPartitions,
                    "Selected disk already has partitions or filesystems.");
            }
        }

        var expectedIdentity = StorageValidationHelpers.NormalizeDiskIdentity(payload.ExpectedSerial, null);
        var actualIdentity = StorageValidationHelpers.NormalizeDiskIdentity(diskInfo.Serial, diskInfo.Wwn);

        if (!string.IsNullOrWhiteSpace(expectedIdentity) &&
            !string.Equals(expectedIdentity, actualIdentity, StringComparison.OrdinalIgnoreCase))
        {
            return PrivilegedHelperResponse.Failure(
                requestId,
                PrivilegedHelperErrorCodes.DiskIdentityMismatch,
                "Disk serial/WWN does not match the expected identity.");
        }

        if (payload.ExpectedSizeBytes > 0 && diskInfo.SizeBytes != payload.ExpectedSizeBytes)
        {
            return PrivilegedHelperResponse.Failure(
                requestId,
                PrivilegedHelperErrorCodes.DiskIdentityMismatch,
                "Disk size does not match the expected size.");
        }

        return null;
    }

    private async Task<PrivilegedHelperResponse?> TryCompleteExistingConfigurationAsync(
        string requestId,
        StorageConfigurePayload payload,
        string diskPath,
        string mountPath,
        string partitionPath,
        string fileSystem,
        List<string> completedSteps,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(partitionPath))
        {
            return null;
        }

        completedSteps.Add("DetectExistingPartition");

        var uuidResult = await _commandRunner.RunAsync(
            "blkid",
            ["-s", "UUID", "-o", "value", partitionPath],
            cancellationToken: cancellationToken);

        if (uuidResult.ExitCode != 0 || string.IsNullOrWhiteSpace(uuidResult.Stdout))
        {
            return FailureFromCommand(requestId, "ReadPartitionUuid", uuidResult, completedSteps);
        }

        var uuid = uuidResult.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries).First().Trim();
        var fstabUpdated = false;

        Directory.CreateDirectory(mountPath);
        completedSteps.Add("CreateMountDirectory");

        var backupStep = await BackupFstabStepAsync(completedSteps, cancellationToken);
        if (backupStep is not null)
        {
            return backupStep;
        }

        var updateFstab = await UpdateFstabStepAsync(
            requestId,
            partitionPath,
            mountPath,
            fileSystem,
            completedSteps,
            cancellationToken);
        if (updateFstab is not null)
        {
            return updateFstab;
        }

        if (File.Exists("/etc/fstab"))
        {
            var fstabContent = await File.ReadAllTextAsync("/etc/fstab", cancellationToken);
            fstabUpdated = fstabContent.Contains($"UUID={uuid}", StringComparison.Ordinal);
        }

        var mountFailure = await MountStepAsync(
            requestId,
            mountPath,
            partitionPath,
            fileSystem,
            uuid,
            completedSteps,
            cancellationToken);
        if (mountFailure is not null)
        {
            return mountFailure;
        }

        var verifyFailure = await VerifyMountStepAsync(requestId, mountPath, completedSteps, cancellationToken);
        if (verifyFailure is not null)
        {
            return verifyFailure;
        }

        var subdirFailure = await CreateSubdirectoriesStepAsync(mountPath, completedSteps, cancellationToken);
        if (subdirFailure is not null)
        {
            return subdirFailure;
        }

        var ownershipFailure = await SetOwnershipStepAsync(
            requestId,
            mountPath,
            payload.OwnerUser,
            payload.OwnerGroup,
            completedSteps,
            cancellationToken);
        if (ownershipFailure is not null)
        {
            return ownershipFailure;
        }

        var permissionsFailure = await SetPermissionsStepAsync(
            requestId,
            mountPath,
            payload.DirectoryMode,
            completedSteps,
            cancellationToken);
        if (permissionsFailure is not null)
        {
            return permissionsFailure;
        }

        var createdDirectories = StorageSubdirectories
            .Select(name => Path.Combine(mountPath, name))
            .ToList();

        var data = JsonSerializer.SerializeToElement(new StorageConfigureResultData
        {
            DiskPath = diskPath,
            PartitionPath = partitionPath,
            MountPath = mountPath,
            FileSystem = fileSystem,
            Uuid = uuid,
            CreatedDirectories = createdDirectories,
            FstabUpdated = fstabUpdated,
            Mounted = true
        }, JsonOptions);

        return PrivilegedHelperResponse.Ok(
            requestId,
            "Storage configuration completed.",
            completedSteps,
            data);
    }

    private async Task<PrivilegedHelperResponse?> RunStepAsync(
        string requestId,
        string executableKey,
        IReadOnlyList<string> arguments,
        List<string> completedSteps,
        string stepName,
        CancellationToken cancellationToken)
    {
        var result = await _commandRunner.RunAsync(executableKey, arguments, cancellationToken: cancellationToken);
        if (result.ExitCode != 0)
        {
            return FailureFromCommand(requestId, stepName, result, completedSteps);
        }

        completedSteps.Add(stepName);
        return null;
    }

    private static Task<PrivilegedHelperResponse?> CreateDirectoryStepAsync(
        string mountPath,
        List<string> completedSteps,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(mountPath);
        completedSteps.Add("CreateMountDirectory");
        return Task.FromResult<PrivilegedHelperResponse?>(null);
    }

    private async Task<PrivilegedHelperResponse?> WaitForPartitionAsync(
        string requestId,
        string partitionPath,
        List<string> completedSteps,
        CancellationToken cancellationToken)
    {
        const int maxAttempts = 10;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(partitionPath))
            {
                completedSteps.Add("WaitForPartition");
                return null;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
        }

        return PrivilegedHelperResponse.Failure(
            requestId,
            PrivilegedHelperErrorCodes.CommandFailed,
            $"Partition device '{partitionPath}' was not created in time.",
            failedStep: "WaitForPartition",
            completedSteps: completedSteps);
    }

    private static Task<PrivilegedHelperResponse?> BackupFstabStepAsync(
        List<string> completedSteps,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists("/etc/fstab"))
        {
            completedSteps.Add("BackupFstab");
            return Task.FromResult<PrivilegedHelperResponse?>(null);
        }

        var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        var backupPath = $"/etc/fstab.excpanel-backup-{timestamp}";
        File.Copy("/etc/fstab", backupPath, overwrite: false);
        completedSteps.Add("BackupFstab");
        return Task.FromResult<PrivilegedHelperResponse?>(null);
    }

    private async Task<PrivilegedHelperResponse?> UpdateFstabStepAsync(
        string requestId,
        string partitionPath,
        string mountPath,
        string fileSystem,
        List<string> completedSteps,
        CancellationToken cancellationToken)
    {
        var uuidResult = await _commandRunner.RunAsync(
            "blkid",
            ["-s", "UUID", "-o", "value", partitionPath],
            cancellationToken: cancellationToken);

        if (uuidResult.ExitCode != 0 || string.IsNullOrWhiteSpace(uuidResult.Stdout))
        {
            return FailureFromCommand(requestId, "UpdateFstab", uuidResult, completedSteps);
        }

        var uuid = uuidResult.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries).First().Trim();
        var fstabLine = $"UUID={uuid} {mountPath} {fileSystem} defaults,nofail 0 2";

        if (File.Exists("/etc/fstab"))
        {
            var existing = await File.ReadAllTextAsync("/etc/fstab", cancellationToken);
            if (existing.Contains($"UUID={uuid}", StringComparison.Ordinal) ||
                existing.Split('\n').Any(line => line.Contains(mountPath, StringComparison.Ordinal)))
            {
                completedSteps.Add("UpdateFstab");
                return null;
            }
        }

        var tempPath = $"/etc/fstab.excpanel-temp-{Guid.NewGuid():N}";
        try
        {
            var content = File.Exists("/etc/fstab")
                ? await File.ReadAllTextAsync("/etc/fstab", cancellationToken)
                : string.Empty;

            if (!content.EndsWith('\n') && content.Length > 0)
            {
                content += Environment.NewLine;
            }

            content += fstabLine + Environment.NewLine;
            await File.WriteAllTextAsync(tempPath, content, cancellationToken);
            File.Move(tempPath, "/etc/fstab", overwrite: true);
            completedSteps.Add("UpdateFstab");
            return null;
        }
        catch (Exception ex)
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            return PrivilegedHelperResponse.Failure(
                requestId,
                PrivilegedHelperErrorCodes.CommandFailed,
                "Failed to update /etc/fstab atomically.",
                failedStep: "UpdateFstab",
                completedSteps: completedSteps,
                stderr: ex.Message);
        }
    }

    private async Task<PrivilegedHelperResponse?> MountStepAsync(
        string requestId,
        string mountPath,
        string partitionPath,
        string fileSystem,
        string? uuid,
        List<string> completedSteps,
        CancellationToken cancellationToken)
    {
        var alreadyMounted = await _commandRunner.RunAsync(
            "findmnt",
            ["-n", "-M", mountPath],
            cancellationToken: cancellationToken);

        if (alreadyMounted.ExitCode == 0)
        {
            completedSteps.Add("MountStoragePath");
            return null;
        }

        var attempts = new List<IReadOnlyList<string>>
        {
            new[] { mountPath },
            new[] { "-t", fileSystem, partitionPath, mountPath }
        };

        if (!string.IsNullOrWhiteSpace(uuid))
        {
            attempts.Add(new[] { "-t", fileSystem, $"UUID={uuid}", mountPath });
        }

        CommandExecutionResult? lastResult = null;
        foreach (var arguments in attempts)
        {
            var result = await _commandRunner.RunAsync("mount", arguments, cancellationToken: cancellationToken);
            if (result.ExitCode == 0)
            {
                completedSteps.Add("MountStoragePath");
                return null;
            }

            lastResult = result;
        }

        return FailureFromCommand(requestId, "MountStoragePath", lastResult!, completedSteps);
    }

    private async Task<PrivilegedHelperResponse?> VerifyMountStepAsync(
        string requestId,
        string mountPath,
        List<string> completedSteps,
        CancellationToken cancellationToken)
    {
        var result = await _commandRunner.RunAsync(
            "findmnt",
            ["-n", "-M", mountPath],
            cancellationToken: cancellationToken);

        if (result.ExitCode != 0)
        {
            return PrivilegedHelperResponse.Failure(
                requestId,
                PrivilegedHelperErrorCodes.CommandFailed,
                $"Mount path '{mountPath}' is not mounted after configuration.",
                failedStep: "VerifyMount",
                completedSteps: completedSteps,
                exitCode: result.ExitCode,
                stderr: result.Stderr);
        }

        completedSteps.Add("VerifyMount");
        return null;
    }

    private static Task<PrivilegedHelperResponse?> CreateSubdirectoriesStepAsync(
        string mountPath,
        List<string> completedSteps,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        foreach (var subdirectory in StorageSubdirectories)
        {
            Directory.CreateDirectory(Path.Combine(mountPath, subdirectory));
        }

        completedSteps.Add("CreateSubdirectories");
        return Task.FromResult<PrivilegedHelperResponse?>(null);
    }

    private async Task<PrivilegedHelperResponse?> SetOwnershipStepAsync(
        string requestId,
        string mountPath,
        string ownerUser,
        string ownerGroup,
        List<string> completedSteps,
        CancellationToken cancellationToken)
    {
        var result = await _commandRunner.RunAsync(
            "chown",
            ["-R", $"{ownerUser}:{ownerGroup}", mountPath],
            cancellationToken: cancellationToken);

        if (result.ExitCode != 0)
        {
            return FailureFromCommand(requestId, "SetOwnership", result, completedSteps);
        }

        completedSteps.Add("SetOwnership");
        return null;
    }

    private async Task<PrivilegedHelperResponse?> SetPermissionsStepAsync(
        string requestId,
        string mountPath,
        string directoryMode,
        List<string> completedSteps,
        CancellationToken cancellationToken)
    {
        var mode = directoryMode.StartsWith('0') ? directoryMode : $"0{directoryMode}";
        var result = await _commandRunner.RunAsync(
            "chmod",
            ["-R", mode, mountPath],
            cancellationToken: cancellationToken);

        if (result.ExitCode != 0)
        {
            return FailureFromCommand(requestId, "SetPermissions", result, completedSteps);
        }

        completedSteps.Add("SetPermissions");
        return null;
    }

    private static PrivilegedHelperResponse FailureFromCommand(
        string requestId,
        string step,
        CommandExecutionResult result,
        IReadOnlyList<string> completedSteps) =>
        PrivilegedHelperResponse.Failure(
            requestId,
            PrivilegedHelperErrorCodes.CommandFailed,
            $"Storage configuration failed at step '{step}'.",
            failedStep: step,
            completedSteps: completedSteps,
            exitCode: result.ExitCode,
            stderr: result.Stderr);
}
