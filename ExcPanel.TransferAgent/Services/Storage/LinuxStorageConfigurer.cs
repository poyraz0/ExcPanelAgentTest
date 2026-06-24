using ExcPanel.TransferAgent.Contracts;
using ExcPanel.TransferAgent.Models;
using ExcPanel.TransferAgent.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExcPanel.TransferAgent.Services.Storage;

/// <summary>
/// DESTRUCTIVE: Executes disk partitioning, formatting, fstab updates and mount operations.
/// Only invoke after all safety validations and confirmation code checks pass.
/// </summary>
public class LinuxStorageConfigurer
{
    private static readonly string[] StorageSubdirectories = ["exports", "imports", "temp", "logs"];

    private readonly ICommandRunner _commandRunner;
    private readonly IStorageSettingsWriter _storageSettingsWriter;
    private readonly TransferAgentOptions _options;
    private readonly ILogger<LinuxStorageConfigurer> _logger;

    public LinuxStorageConfigurer(
        ICommandRunner commandRunner,
        IStorageSettingsWriter storageSettingsWriter,
        IOptions<TransferAgentOptions> options,
        ILogger<LinuxStorageConfigurer> logger)
    {
        _commandRunner = commandRunner;
        _storageSettingsWriter = storageSettingsWriter;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ConfigureStorageOperationResult> ConfigureAsync(
        ConfigureStorageRequest request,
        StorageDiskInfo disk,
        CancellationToken cancellationToken = default)
    {
        var (canElevate, privilegeError) = await PrivilegeHelper.CheckPrivilegesAsync(
            _commandRunner,
            _options.UseSudoForPrivilegedCommands,
            cancellationToken);

        if (!canElevate)
        {
            return new ConfigureStorageOperationResult
            {
                Status = ConfigureStorageStatus.ValidationFailed,
                Message = "Insufficient privileges for storage configuration.",
                Failure = new ConfigureStorageFailureResponse
                {
                    FailedStep = "PrivilegeCheck",
                    ValidationErrors = [privilegeError ?? "Insufficient privileges."]
                }
            };
        }

        var diskPath = request.DiskPath.Trim();
        var mountPath = request.MountPath.Trim();
        var partitionPath = StorageConfigureValidation.ResolvePartitionPath(diskPath);
        var fileSystem = request.FileSystem.Trim().ToLowerInvariant();

        var steps = new (string Step, Func<Task<CommandResult?>> Action)[]
        {
            ("CreateGptPartitionTable", () => RunRequiredAsync("parted", $"{diskPath} --script mklabel gpt", cancellationToken)),
            ("CreatePrimaryPartition", () => RunRequiredAsync("parted", $"{diskPath} --script mkpart primary {fileSystem} 0% 100%", cancellationToken)),
            ("ProbePartitionTable", () => RunRequiredAsync("partprobe", diskPath, cancellationToken)),
            ("FormatPartition", () => RunRequiredAsync("mkfs.ext4", $"-F {partitionPath}", cancellationToken)),
            ("CreateMountDirectory", () => RunRequiredAsync("mkdir", $"-p {mountPath}", cancellationToken))
        };

        foreach (var (step, action) in steps)
        {
            var commandResult = await action();
            if (commandResult is not null)
            {
                return CommandFailure(step, commandResult);
            }
        }

        var uuidResult = await _commandRunner.RunAsync("blkid", $"-s UUID -o value {partitionPath}", privileged: true, cancellationToken: cancellationToken);
        if (uuidResult.ExitCode != 0 || string.IsNullOrWhiteSpace(uuidResult.Stdout))
        {
            return CommandFailure("ReadPartitionUuid", uuidResult);
        }

        var uuid = uuidResult.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries).First().Trim();
        var fstabUpdated = false;

        if (!await FstabEntryExistsAsync(uuid, mountPath, cancellationToken))
        {
            var backupStep = await BackupFstabAsync(cancellationToken);
            if (backupStep is not null)
            {
                return backupStep;
            }

            var appendResult = await AppendFstabEntryAsync(uuid, mountPath, fileSystem, cancellationToken);
            if (appendResult.ExitCode != 0)
            {
                return CommandFailure("AppendFstabEntry", appendResult);
            }

            fstabUpdated = true;
        }

        var mountResult = await _commandRunner.RunAsync("mount", mountPath, privileged: true, cancellationToken: cancellationToken);
        if (mountResult.ExitCode != 0)
        {
            return CommandFailure("MountStoragePath", mountResult);
        }

        var createdDirectories = new List<string>();
        foreach (var subdirectory in StorageSubdirectories)
        {
            var directoryPath = Path.Combine(mountPath, subdirectory);
            var mkdirResult = await _commandRunner.RunAsync("mkdir", $"-p {directoryPath}", privileged: true, cancellationToken: cancellationToken);
            if (mkdirResult.ExitCode != 0)
            {
                return CommandFailure($"CreateDirectory:{subdirectory}", mkdirResult);
            }

            createdDirectories.Add(directoryPath);
        }

        var ownerUser = ResolveOwnerUser();
        var ownerGroup = ResolveOwnerGroup();
        var chownResult = await _commandRunner.RunAsync("chown", $"-R {ownerUser}:{ownerGroup} {mountPath}", privileged: true, cancellationToken: cancellationToken);
        if (chownResult.ExitCode != 0)
        {
            return CommandFailure("SetOwnership", chownResult);
        }

        var chmodResult = await _commandRunner.RunAsync("chmod", $"-R {_options.StorageDirectoryMode} {mountPath}", privileged: true, cancellationToken: cancellationToken);
        if (chmodResult.ExitCode != 0)
        {
            return CommandFailure("SetPermissions", chmodResult);
        }

        var mounted = await VerifyMountAsync(mountPath, cancellationToken);
        if (!mounted)
        {
            return new ConfigureStorageOperationResult
            {
                Status = ConfigureStorageStatus.CommandFailed,
                Message = "Mount verification failed after configuration.",
                Failure = new ConfigureStorageFailureResponse
                {
                    FailedStep = "VerifyMount",
                    ValidationErrors = [$"Mount path '{mountPath}' is not mounted after configuration."]
                }
            };
        }

        await _storageSettingsWriter.WriteStorageRootPathAsync(mountPath, cancellationToken);

        return new ConfigureStorageOperationResult
        {
            Status = ConfigureStorageStatus.Success,
            Success = new ConfigureStorageResponse
            {
                Configured = true,
                DiskPath = diskPath,
                PartitionPath = partitionPath,
                MountPath = mountPath,
                FileSystem = fileSystem,
                Uuid = uuid,
                CreatedDirectories = createdDirectories,
                FstabUpdated = fstabUpdated,
                Mounted = true
            }
        };
    }

    private async Task<CommandResult?> RunRequiredAsync(string command, string args, CancellationToken cancellationToken)
    {
        var result = await _commandRunner.RunAsync(command, args, privileged: true, cancellationToken: cancellationToken);
        return result.ExitCode == 0 ? null : result;
    }

    private async Task<ConfigureStorageOperationResult?> BackupFstabAsync(CancellationToken cancellationToken)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        var backupPath = $"/etc/fstab.excpanel-backup-{timestamp}";
        var backupResult = await _commandRunner.RunAsync("cp", $"/etc/fstab {backupPath}", privileged: true, cancellationToken: cancellationToken);
        return backupResult.ExitCode == 0 ? null : CommandFailure("BackupFstab", backupResult);
    }

    private async Task<CommandResult> AppendFstabEntryAsync(
        string uuid,
        string mountPath,
        string fileSystem,
        CancellationToken cancellationToken)
    {
        var fstabLine = $"UUID={uuid} {mountPath} {fileSystem} defaults,nofail 0 2";
        return await _commandRunner.RunAsync("bash", $"-c \"echo '{fstabLine}' >> /etc/fstab\"", privileged: true, cancellationToken: cancellationToken);
    }

    private static async Task<bool> FstabEntryExistsAsync(string uuid, string mountPath, CancellationToken cancellationToken)
    {
        if (!File.Exists("/etc/fstab"))
        {
            return false;
        }

        var fstabContent = await File.ReadAllTextAsync("/etc/fstab", cancellationToken);
        return fstabContent.Contains($"UUID={uuid}", StringComparison.Ordinal) ||
               fstabContent.Split('\n').Any(line => line.Contains(mountPath, StringComparison.Ordinal));
    }

    private async Task<bool> VerifyMountAsync(string mountPath, CancellationToken cancellationToken)
    {
        var result = await _commandRunner.RunAsync("findmnt", $"-n -M {mountPath}", cancellationToken: cancellationToken);
        return result.ExitCode == 0;
    }

    private string ResolveOwnerUser()
    {
        var currentUser = Environment.UserName;
        return string.IsNullOrWhiteSpace(currentUser) ? _options.StorageOwnerUser : currentUser;
    }

    private string ResolveOwnerGroup()
    {
        try
        {
            var group = Environment.GetEnvironmentVariable("USER") ?? Environment.UserName;
            if (!string.IsNullOrWhiteSpace(group))
            {
                return group;
            }
        }
        catch
        {
            // Fall back to configured group.
        }

        return _options.StorageOwnerGroup;
    }

    private static ConfigureStorageOperationResult CommandFailure(string step, CommandResult commandResult)
    {
        var permissionHint = commandResult.Stderr.Contains("Permission denied", StringComparison.OrdinalIgnoreCase) ||
                             commandResult.Stderr.Contains("Operation not permitted", StringComparison.OrdinalIgnoreCase)
            ? "Insufficient privileges. Run the transfer agent with root or required sudo permissions."
            : null;

        var validationErrors = new List<string> { $"Step '{step}' failed." };
        if (!string.IsNullOrWhiteSpace(permissionHint))
        {
            validationErrors.Add(permissionHint);
        }

        return new ConfigureStorageOperationResult
        {
            Status = ConfigureStorageStatus.CommandFailed,
            Message = $"Storage configuration failed at step '{step}'.",
            Failure = new ConfigureStorageFailureResponse
            {
                FailedStep = step,
                ValidationErrors = validationErrors,
                FailedCommand = commandResult
            }
        };
    }
}
