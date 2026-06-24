using System.Diagnostics;
using System.Text.Json;
using ExcPanel.TransferAgent.Contracts;
using ExcPanel.TransferAgent.PrivilegedHelper.Commands;

namespace ExcPanel.TransferAgent.PrivilegedHelper.Handlers;

public class SftpUserHandler
{
    private const string NologinShell = "/usr/sbin/nologin";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IPrivilegedCommandRunner _commandRunner;

    public SftpUserHandler(IPrivilegedCommandRunner commandRunner)
    {
        _commandRunner = commandRunner;
    }

    public async Task<PrivilegedHelperResponse> HandleCreateAsync(
        string requestId,
        JsonElement payloadElement,
        CancellationToken cancellationToken)
    {
        var payload = DeserializePayload<SftpUserCreatePayload>(payloadElement);
        if (payload is null)
        {
            return InvalidArguments(requestId, "sftp.user.create payload is invalid.");
        }

        if (payload.JobType == SftpJobType.Import)
        {
            return PrivilegedHelperResponse.Failure(
                requestId,
                PrivilegedHelperErrorCodes.SftpNotImplemented,
                "Import SFTP user creation is not implemented yet.");
        }

        var validationError = ValidateCreatePayload(requestId, payload, out var normalizedJobPath, out var chrootPath, out var filesMountPath, out var groupName);
        if (validationError is not null)
        {
            return validationError;
        }

        var completedSteps = new List<string>();
        var userExists = UnixIdentity.Exists(payload.Username);
        var bindMountWasActive = await SftpBindMountHelper.IsBindMountActiveAsync(
            _commandRunner,
            filesMountPath,
            normalizedJobPath,
            cancellationToken);

        var filesystemResult = await EnsureExportFilesystemAsync(
            requestId,
            chrootPath,
            filesMountPath,
            normalizedJobPath,
            groupName,
            completedSteps,
            cancellationToken);
        if (filesystemResult is not null)
        {
            return filesystemResult;
        }

        var idempotent = false;

        if (userExists)
        {
            idempotent = true;
            completedSteps.Add("ExistingUser");
        }
        else
        {
            var expiryDate = SftpLinuxAccountExpiry.ComputeExpiryDate(payload.ExpiresAtUtc);
            var userAddResult = await _commandRunner.RunAsync(
                "useradd",
                [
                    "-M",
                    "-d", chrootPath,
                    "-s", NologinShell,
                    "-g", groupName,
                    "-e", expiryDate,
                    payload.Username
                ],
                cancellationToken: cancellationToken);
            if (userAddResult.ExitCode != 0)
            {
                return FailureFromCommand(requestId, "CreateLinuxUser", userAddResult, completedSteps);
            }

            completedSteps.Add("CreateLinuxUser");
        }

        var passwordResult = await SetPasswordAsync(payload.Username, payload.PlaintextPassword, cancellationToken);
        if (passwordResult.ExitCode != 0)
        {
            return FailureFromCommand(requestId, "SetPassword", passwordResult, completedSteps);
        }

        completedSteps.Add("SetPassword");

        if (IsPasswordLocked(payload.Username))
        {
            var unlockResult = await _commandRunner.RunAsync(
                "passwd",
                ["-u", payload.Username],
                cancellationToken: cancellationToken);
            if (unlockResult.ExitCode != 0)
            {
                return FailureFromCommand(requestId, "UnlockPassword", unlockResult, completedSteps);
            }

            completedSteps.Add("UnlockPassword");
        }

        var bindMountActive = await SftpBindMountHelper.IsBindMountActiveAsync(
            _commandRunner,
            filesMountPath,
            normalizedJobPath,
            cancellationToken);
        if (!bindMountActive)
        {
            return PrivilegedHelperResponse.Failure(
                requestId,
                PrivilegedHelperErrorCodes.BindMountConflict,
                "SFTP bind mount is not active after user filesystem preparation.",
                failedStep: "VerifyBindMount",
                completedSteps: completedSteps);
        }

        var data = JsonSerializer.SerializeToElement(new SftpUserCreateResultData
        {
            Username = payload.Username,
            ChrootPath = chrootPath,
            FilesMountPath = filesMountPath,
            JobPath = normalizedJobPath,
            JobType = payload.JobType,
            GroupName = groupName,
            UserCreated = !idempotent,
            BindMountCreated = !bindMountWasActive && bindMountActive,
            Idempotent = idempotent
        }, JsonOptions);

        return PrivilegedHelperResponse.Ok(
            requestId,
            idempotent ? "SFTP user already exists." : "SFTP user created successfully.",
            completedSteps,
            data);
    }

    public async Task<PrivilegedHelperResponse> HandleDisableAsync(
        string requestId,
        JsonElement payloadElement,
        CancellationToken cancellationToken)
    {
        var payload = DeserializePayload<SftpUserDisablePayload>(payloadElement);
        if (payload is null)
        {
            return InvalidArguments(requestId, "sftp.user.disable payload is invalid.");
        }

        if (!SftpValidationHelpers.IsValidUsername(payload.Username))
        {
            return PrivilegedHelperResponse.Failure(
                requestId,
                PrivilegedHelperErrorCodes.InvalidUsername,
                "Username is invalid.");
        }

        if (!UnixIdentity.Exists(payload.Username))
        {
            return PrivilegedHelperResponse.Failure(
                requestId,
                PrivilegedHelperErrorCodes.UserNotFound,
                "SFTP user was not found.");
        }

        var completedSteps = new List<string>();
        var alreadyLocked = IsPasswordLocked(payload.Username);
        if (!alreadyLocked)
        {
            var lockResult = await _commandRunner.RunAsync(
                "passwd",
                ["-l", payload.Username],
                cancellationToken: cancellationToken);
            if (lockResult.ExitCode != 0)
            {
                return FailureFromCommand(requestId, "LockPassword", lockResult, completedSteps);
            }
        }

        completedSteps.Add("LockPassword");

        var data = JsonSerializer.SerializeToElement(new SftpUserDisableResultData
        {
            Username = payload.Username,
            PasswordLocked = true,
            AlreadyDisabled = alreadyLocked
        }, JsonOptions);

        return PrivilegedHelperResponse.Ok(
            requestId,
            "SFTP user disabled successfully.",
            completedSteps,
            data);
    }

    public async Task<PrivilegedHelperResponse> HandleDeleteAsync(
        string requestId,
        JsonElement payloadElement,
        CancellationToken cancellationToken)
    {
        var payload = DeserializePayload<SftpUserDeletePayload>(payloadElement);
        if (payload is null)
        {
            return InvalidArguments(requestId, "sftp.user.delete payload is invalid.");
        }

        if (!SftpValidationHelpers.IsValidUsername(payload.Username))
        {
            return PrivilegedHelperResponse.Failure(
                requestId,
                PrivilegedHelperErrorCodes.InvalidUsername,
                "Username is invalid.");
        }

        var auditSteps = new List<string>();
        var completedSteps = new List<string>();

        if (UnixIdentity.Exists(payload.Username))
        {
            if (!IsPasswordLocked(payload.Username))
            {
                var lockResult = await _commandRunner.RunAsync(
                    "passwd",
                    ["-l", payload.Username],
                    cancellationToken: cancellationToken);
                if (lockResult.ExitCode != 0)
                {
                    return FailureFromCommand(requestId, "DisableBeforeDelete", lockResult, completedSteps);
                }

                auditSteps.Add("DisabledUser");
                completedSteps.Add("DisableBeforeDelete");
            }

            if (SftpProcessInspector.HasActiveSession(payload.Username))
            {
                return PrivilegedHelperResponse.Failure(
                    requestId,
                    PrivilegedHelperErrorCodes.ActiveSessionConflict,
                    "SFTP user has an active session and cannot be deleted.",
                    failedStep: "CheckActiveSession",
                    completedSteps: completedSteps);
            }

            auditSteps.Add("CheckedActiveSessions");
        }

        var bindMountRemoved = false;
        var unmountResult = await UnmountFilesMountAsync(
            requestId,
            payload.FilesMountPath,
            completedSteps,
            cancellationToken);
        if (unmountResult.Failure is not null)
        {
            return unmountResult.Failure;
        }

        if (unmountResult.Removed)
        {
            bindMountRemoved = true;
            auditSteps.Add("RemovedBindMount");
        }

        var userDeleted = false;
        if (UnixIdentity.Exists(payload.Username))
        {
            var deleteResult = await _commandRunner.RunAsync(
                "userdel",
                [payload.Username],
                cancellationToken: cancellationToken);
            if (deleteResult.ExitCode != 0)
            {
                return FailureFromCommand(requestId, "DeleteLinuxUser", deleteResult, completedSteps);
            }

            userDeleted = true;
            auditSteps.Add("DeletedLinuxUser");
            completedSteps.Add("DeleteLinuxUser");
        }
        else
        {
            userDeleted = true;
            auditSteps.Add("LinuxUserAlreadyAbsent");
            completedSteps.Add("LinuxUserAlreadyAbsent");
        }

        var chrootRemoved = TryRemoveChroot(
            payload.ChrootPath,
            payload.FilesMountPath,
            auditSteps,
            completedSteps);

        auditSteps.Add("PreservedJobPath");

        var data = JsonSerializer.SerializeToElement(new SftpUserDeleteResultData
        {
            Username = payload.Username,
            UserDeleted = userDeleted,
            BindMountRemoved = bindMountRemoved,
            ChrootRemoved = chrootRemoved,
            JobPathPreserved = true,
            AuditSteps = auditSteps
        }, JsonOptions);

        return PrivilegedHelperResponse.Ok(
            requestId,
            "SFTP user deleted successfully.",
            completedSteps,
            data);
    }

    public async Task<PrivilegedHelperResponse> HandleStatusAsync(
        string requestId,
        JsonElement payloadElement,
        CancellationToken cancellationToken)
    {
        var payload = DeserializePayload<SftpUserStatusPayload>(payloadElement);
        if (payload is null)
        {
            return InvalidArguments(requestId, "sftp.user.status payload is invalid.");
        }

        if (!SftpValidationHelpers.IsValidUsername(payload.Username))
        {
            return PrivilegedHelperResponse.Failure(
                requestId,
                PrivilegedHelperErrorCodes.InvalidUsername,
                "Username is invalid.");
        }

        var bindMountActive = await SftpBindMountHelper.IsBindMountActiveAsync(
            _commandRunner,
            payload.FilesMountPath,
            payload.JobPath,
            cancellationToken);
        var bindMountRestored = false;

        if (payload.EnsureBindMount)
        {
            if (!Directory.Exists(payload.JobPath))
            {
                return PrivilegedHelperResponse.Failure(
                    requestId,
                    PrivilegedHelperErrorCodes.InvalidJobPath,
                    "Job directory does not exist.");
            }

            var completedSteps = new List<string>();
            var wasActive = bindMountActive;
            var ensureResult = await EnsureExportFilesystemAsync(
                requestId,
                payload.ChrootPath,
                payload.FilesMountPath,
                payload.JobPath,
                payload.ExportGroupName,
                completedSteps,
                cancellationToken);
            if (ensureResult is not null)
            {
                return ensureResult;
            }

            bindMountActive = await SftpBindMountHelper.IsBindMountActiveAsync(
                _commandRunner,
                payload.FilesMountPath,
                payload.JobPath,
                cancellationToken);
            if (!bindMountActive)
            {
                return PrivilegedHelperResponse.Failure(
                    requestId,
                    PrivilegedHelperErrorCodes.BindMountConflict,
                    "SFTP bind mount could not be restored.",
                    failedStep: "VerifyBindMount",
                    completedSteps: completedSteps);
            }

            bindMountRestored = !wasActive && bindMountActive;
        }

        var data = JsonSerializer.SerializeToElement(new SftpUserStatusResultData
        {
            Username = payload.Username,
            LinuxUserExists = UnixIdentity.Exists(payload.Username),
            PasswordLocked = IsPasswordLocked(payload.Username),
            BindMountActive = bindMountActive,
            ChrootExists = Directory.Exists(payload.ChrootPath),
            ObservedJobPath = bindMountActive ? payload.JobPath : null,
            BindMountRestored = bindMountRestored
        }, JsonOptions);

        return PrivilegedHelperResponse.Ok(
            requestId,
            "SFTP user status retrieved.",
            Array.Empty<string>(),
            data);
    }

    private PrivilegedHelperResponse? ValidateCreatePayload(
        string requestId,
        SftpUserCreatePayload payload,
        out string normalizedJobPath,
        out string chrootPath,
        out string filesMountPath,
        out string groupName)
    {
        normalizedJobPath = string.Empty;
        chrootPath = string.Empty;
        filesMountPath = string.Empty;
        groupName = string.Empty;

        if (!SftpValidationHelpers.IsValidUsername(payload.Username))
        {
            return PrivilegedHelperResponse.Failure(
                requestId,
                PrivilegedHelperErrorCodes.InvalidUsername,
                "Username is invalid.");
        }

        if (string.IsNullOrWhiteSpace(payload.PlaintextPassword) || payload.PlaintextPassword.Length < 24)
        {
            return PrivilegedHelperResponse.Failure(
                requestId,
                PrivilegedHelperErrorCodes.InvalidArguments,
                "Password must be at least 24 characters.");
        }

        if (!Guid.TryParse(payload.JobId, out _))
        {
            return PrivilegedHelperResponse.Failure(
                requestId,
                PrivilegedHelperErrorCodes.InvalidArguments,
                "jobId must be a valid GUID.");
        }

        if (!SftpValidationHelpers.TryResolveJobPath(payload.StorageRootPath, payload.JobPath, out normalizedJobPath, out var jobPathError))
        {
            return PrivilegedHelperResponse.Failure(
                requestId,
                PrivilegedHelperErrorCodes.InvalidJobPath,
                jobPathError ?? "Job path is invalid.");
        }

        if (!Directory.Exists(normalizedJobPath))
        {
            return PrivilegedHelperResponse.Failure(
                requestId,
                PrivilegedHelperErrorCodes.InvalidJobPath,
                "Job directory does not exist.");
        }

        if (!SftpValidationHelpers.TryValidateMarker(
                normalizedJobPath,
                payload.MarkerFileName,
                payload.JobId,
                payload.JobType,
                out var markerError))
        {
            return PrivilegedHelperResponse.Failure(
                requestId,
                markerError?.Contains("missing", StringComparison.OrdinalIgnoreCase) == true
                    ? PrivilegedHelperErrorCodes.JobMarkerMissing
                    : PrivilegedHelperErrorCodes.JobMarkerMismatch,
                markerError ?? "Job marker validation failed.");
        }

        chrootPath = SftpValidationHelpers.BuildChrootPath(payload.ChrootBasePath, payload.Username);
        filesMountPath = SftpValidationHelpers.BuildFilesMountPath(chrootPath);
        groupName = payload.JobType == SftpJobType.Export
            ? payload.ExportGroupName
            : payload.ImportGroupName;

        if (!UnixIdentity.ExistsGroup(groupName))
        {
            return PrivilegedHelperResponse.Failure(
                requestId,
                PrivilegedHelperErrorCodes.SftpNotInitialized,
                "SFTP groups are not initialized.");
        }

        return null;
    }

    private async Task<PrivilegedHelperResponse?> GrantJobDirectoryReadAccessAsync(
        string requestId,
        string jobPath,
        string groupName,
        List<string> completedSteps,
        CancellationToken cancellationToken)
    {
        var chgrpResult = await _commandRunner.RunAsync(
            "chown",
            ["-R", $":{groupName}", jobPath],
            cancellationToken: cancellationToken);
        if (chgrpResult.ExitCode != 0)
        {
            return FailureFromCommand(requestId, "GrantJobDirectoryGroup", chgrpResult, completedSteps);
        }

        var chmodResult = await _commandRunner.RunAsync(
            "chmod",
            ["-R", "u=rwX,g=rX,o=", jobPath],
            cancellationToken: cancellationToken);
        if (chmodResult.ExitCode != 0)
        {
            return FailureFromCommand(requestId, "GrantJobDirectoryMode", chmodResult, completedSteps);
        }

        var directoryModeResult = await _commandRunner.RunAsync(
            "chmod",
            ["2750", jobPath],
            cancellationToken: cancellationToken);
        if (directoryModeResult.ExitCode != 0)
        {
            return FailureFromCommand(requestId, "GrantJobDirectoryInheritance", directoryModeResult, completedSteps);
        }

        completedSteps.Add("GrantJobDirectoryReadAccess");
        return null;
    }

    private async Task<PrivilegedHelperResponse?> EnsureExportFilesystemAsync(
        string requestId,
        string chrootPath,
        string filesMountPath,
        string jobPath,
        string groupName,
        List<string> completedSteps,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(chrootPath))
        {
            Directory.CreateDirectory(chrootPath);
            completedSteps.Add("CreateChrootRoot");
        }

        var chrootChown = await _commandRunner.RunAsync(
            "chown",
            ["root:root", chrootPath],
            cancellationToken: cancellationToken);
        if (chrootChown.ExitCode != 0)
        {
            return FailureFromCommand(requestId, "EnsureChrootOwnership", chrootChown, completedSteps);
        }

        var chrootChmod = await _commandRunner.RunAsync(
            "chmod",
            ["0755", chrootPath],
            cancellationToken: cancellationToken);
        if (chrootChmod.ExitCode != 0)
        {
            return FailureFromCommand(requestId, "EnsureChrootMode", chrootChmod, completedSteps);
        }

        if (!Directory.Exists(filesMountPath))
        {
            Directory.CreateDirectory(filesMountPath);
            completedSteps.Add("CreateFilesMountPoint");
        }

        var filesChown = await _commandRunner.RunAsync(
            "chown",
            ["root:root", filesMountPath],
            cancellationToken: cancellationToken);
        if (filesChown.ExitCode != 0)
        {
            return FailureFromCommand(requestId, "EnsureFilesMountOwnership", filesChown, completedSteps);
        }

        var filesChmod = await _commandRunner.RunAsync(
            "chmod",
            ["0755", filesMountPath],
            cancellationToken: cancellationToken);
        if (filesChmod.ExitCode != 0)
        {
            return FailureFromCommand(requestId, "EnsureFilesMountMode", filesChmod, completedSteps);
        }

        var mountResult = await EnsureBindMountAsync(
            requestId,
            chrootPath,
            filesMountPath,
            jobPath,
            completedSteps,
            cancellationToken);
        if (mountResult is not null)
        {
            return mountResult;
        }

        var grantResult = await GrantJobDirectoryReadAccessAsync(
            requestId,
            jobPath,
            groupName,
            completedSteps,
            cancellationToken);
        if (grantResult is not null)
        {
            return grantResult;
        }

        if (!LinuxBindMountVerifier.IsBindMountActive(filesMountPath, jobPath))
        {
            return PrivilegedHelperResponse.Failure(
                requestId,
                PrivilegedHelperErrorCodes.BindMountConflict,
                "SFTP bind mount verification failed.",
                failedStep: "VerifyBindMount",
                completedSteps: completedSteps);
        }

        return null;
    }

    private async Task<PrivilegedHelperResponse?> EnsureBindMountAsync(
        string requestId,
        string chrootPath,
        string filesMountPath,
        string jobPath,
        List<string> completedSteps,
        CancellationToken cancellationToken)
    {
        if (LinuxBindMountVerifier.IsBindMountActive(filesMountPath, jobPath))
        {
            completedSteps.Add("BindMountAlreadyActive");
            return null;
        }

        var chrootBasePath = Path.GetDirectoryName(chrootPath);
        if (!string.IsNullOrWhiteSpace(chrootBasePath))
        {
            var propagationResult = await SftpHostMountHelper.EnsureSharedMountPropagationAsync(
                _commandRunner,
                chrootBasePath,
                cancellationToken);
            if (propagationResult.ExitCode == 0)
            {
                completedSteps.Add("EnsureChrootMountPropagation");
            }
        }

        if (LinuxBindMountVerifier.IsMountPoint(filesMountPath))
        {
            var unmountResult = await UnmountFilesMountAsync(
                requestId,
                filesMountPath,
                completedSteps,
                cancellationToken);
            if (unmountResult.Failure is not null)
            {
                return unmountResult.Failure;
            }
        }

        if (Directory.Exists(filesMountPath))
        {
            try
            {
                Directory.Delete(filesMountPath, recursive: false);
                completedSteps.Add("ResetFilesMountPoint");
            }
            catch (IOException ex)
            {
                return PrivilegedHelperResponse.Failure(
                    requestId,
                    PrivilegedHelperErrorCodes.BindMountConflict,
                    $"Failed to reset SFTP files mount point: {ex.Message}",
                    failedStep: "ResetFilesMountPoint",
                    completedSteps: completedSteps);
            }
        }

        Directory.CreateDirectory(filesMountPath);
        completedSteps.Add("RecreateFilesMountPoint");

        var filesChown = await _commandRunner.RunAsync(
            "chown",
            ["root:root", filesMountPath],
            cancellationToken: cancellationToken);
        if (filesChown.ExitCode != 0)
        {
            return FailureFromCommand(requestId, "EnsureFilesMountOwnership", filesChown, completedSteps);
        }

        var filesChmod = await _commandRunner.RunAsync(
            "chmod",
            ["0755", filesMountPath],
            cancellationToken: cancellationToken);
        if (filesChmod.ExitCode != 0)
        {
            return FailureFromCommand(requestId, "EnsureFilesMountMode", filesChmod, completedSteps);
        }

        var mountResult = await SftpHostMountHelper.RunInHostMountNamespaceAsync(
            _commandRunner,
            "mount",
            ["--bind", jobPath, filesMountPath],
            cancellationToken);
        if (mountResult.ExitCode != 0)
        {
            return FailureFromCommand(requestId, "EnsureBindMount", mountResult, completedSteps);
        }

        completedSteps.Add("EnsureBindMount");
        return null;
    }

    private async Task<CommandExecutionResult> SetPasswordAsync(
        string username,
        string plaintextPassword,
        CancellationToken cancellationToken) =>
        await _commandRunner.RunAsync(
            "chpasswd",
            Array.Empty<string>(),
            standardInput: $"{username}:{plaintextPassword}\n",
            cancellationToken: cancellationToken);

    private static bool IsPasswordLocked(string username)
    {
        try
        {
            if (!File.Exists("/etc/shadow"))
            {
                return false;
            }

            foreach (var line in File.ReadLines("/etc/shadow"))
            {
                var parts = line.Split(':', StringSplitOptions.None);
                if (parts.Length > 1 && string.Equals(parts[0], username, StringComparison.Ordinal))
                {
                    return parts[1].StartsWith('!') || parts[1].StartsWith('*');
                }
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private async Task<(bool Removed, PrivilegedHelperResponse? Failure)> UnmountFilesMountAsync(
        string requestId,
        string filesMountPath,
        List<string> completedSteps,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(filesMountPath)
            && !LinuxBindMountVerifier.IsMountPoint(filesMountPath))
        {
            return (false, null);
        }

        var attempts = new[]
        {
            new[] { filesMountPath },
            new[] { "-l", filesMountPath }
        };

        foreach (var arguments in attempts)
        {
            var result = await SftpHostMountHelper.RunInHostMountNamespaceAsync(
                _commandRunner,
                "umount",
                arguments,
                cancellationToken);
            if (result.ExitCode == 0)
            {
                completedSteps.Add("UnmountFiles");
                return (true, null);
            }
        }

        if (!LinuxBindMountVerifier.IsMountPoint(filesMountPath))
        {
            return (false, null);
        }

        var lastResult = await SftpHostMountHelper.RunInHostMountNamespaceAsync(
            _commandRunner,
            "umount",
            [filesMountPath],
            cancellationToken);
        return (false, FailureFromCommand(requestId, "UnmountFiles", lastResult, completedSteps));
    }

    private static bool TryRemoveChroot(
        string chrootPath,
        string filesMountPath,
        List<string> auditSteps,
        List<string> completedSteps)
    {
        if (!CanSafelyDeleteChroot(chrootPath, filesMountPath))
        {
            auditSteps.Add("SkippedChrootRemoval:UnexpectedDirectoryLayout");
            return false;
        }

        try
        {
            if (Directory.Exists(filesMountPath))
            {
                Directory.Delete(filesMountPath, recursive: false);
            }

            if (Directory.Exists(chrootPath))
            {
                Directory.Delete(chrootPath, recursive: false);
            }

            auditSteps.Add("RemovedChroot");
            completedSteps.Add("RemoveChroot");
            return true;
        }
        catch (IOException ex)
        {
            auditSteps.Add($"ChrootRemovalSkipped:{ex.Message}");
            return false;
        }
    }

    private static bool CanSafelyDeleteChroot(string chrootPath, string filesMountPath)
    {
        if (!Directory.Exists(chrootPath))
        {
            return false;
        }

        var normalizedFilesMountPath = Path.GetFullPath(filesMountPath);
        var entries = Directory.GetFileSystemEntries(chrootPath).ToList();
        if (entries.Count == 0)
        {
            return true;
        }

        if (entries.Count != 1)
        {
            return false;
        }

        var entryPath = Path.GetFullPath(entries[0].TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.Equals(entryPath, normalizedFilesMountPath, StringComparison.Ordinal);
    }

    private static T? DeserializePayload<T>(JsonElement payloadElement)
    {
        try
        {
            return payloadElement.Deserialize<T>(JsonOptions);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static PrivilegedHelperResponse InvalidArguments(string requestId, string message) =>
        PrivilegedHelperResponse.Failure(requestId, PrivilegedHelperErrorCodes.InvalidArguments, message);

    private static PrivilegedHelperResponse FailureFromCommand(
        string requestId,
        string failedStep,
        CommandExecutionResult result,
        IReadOnlyList<string> completedSteps) =>
        PrivilegedHelperResponse.Failure(
            requestId,
            PrivilegedHelperErrorCodes.CommandFailed,
            $"Command '{result.Executable}' failed during step '{failedStep}'.",
            failedStep: failedStep,
            completedSteps: completedSteps,
            exitCode: result.ExitCode,
            stderr: result.Stderr);
}
