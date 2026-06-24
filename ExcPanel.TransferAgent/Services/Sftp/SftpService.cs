using System.Runtime.InteropServices;
using System.Text.Json;
using ExcPanel.TransferAgent.Contracts;
using ExcPanel.TransferAgent.Models;
using ExcPanel.TransferAgent.Options;
using ExcPanel.TransferAgent.Providers.Linux;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExcPanel.TransferAgent.Services.Sftp;

public class SftpService : ISftpService
{
    private readonly TransferAgentOptions _options;
    private readonly IPrivilegedHelperClient _privilegedHelperClient;
    private readonly ISftpStateStore _stateStore;
    private readonly ISftpAuthorizationService _authorizationService;
    private readonly IStorageMountChecker _mountChecker;
    private readonly IJobDirectoryProvider _jobDirectoryProvider;
    private readonly ISetupConfigStore _setupConfigStore;
    private readonly ILogger<SftpService> _logger;

    public SftpService(
        IOptions<TransferAgentOptions> options,
        IPrivilegedHelperClient privilegedHelperClient,
        ISftpStateStore stateStore,
        ISftpAuthorizationService authorizationService,
        IStorageMountChecker mountChecker,
        IJobDirectoryProvider jobDirectoryProvider,
        ISetupConfigStore setupConfigStore,
        ILogger<SftpService> logger)
    {
        _options = options.Value;
        _privilegedHelperClient = privilegedHelperClient;
        _stateStore = stateStore;
        _authorizationService = authorizationService;
        _mountChecker = mountChecker;
        _jobDirectoryProvider = jobDirectoryProvider;
        _setupConfigStore = setupConfigStore;
        _logger = logger;
    }

    public async Task<SftpOperationResult<SftpInitializeResponse>> InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (!await _authorizationService.AuthorizeAsync(cancellationToken))
        {
            return SftpOperationResult<SftpInitializeResponse>.Fail(SftpOperationStatus.Forbidden, "SFTP operation is not authorized.");
        }

        if (!_options.EnableSftp)
        {
            return SftpOperationResult<SftpInitializeResponse>.Fail(SftpOperationStatus.ServiceUnavailable, "SFTP is disabled.");
        }

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return SftpOperationResult<SftpInitializeResponse>.Fail(SftpOperationStatus.NotSupported, "SFTP is only supported on Linux.");
        }

        var response = await InvokeHelperAsync(
            PrivilegedHelperActions.SftpInitialize,
            BuildInitializePayload(),
            cancellationToken);
        if (!response.Success)
        {
            return MapHelperFailure<SftpInitializeResponse>(response);
        }

        var result = response.Data.Deserialize<SftpInitializeResultData>(PrivilegedHelperJson.SerializerOptions);
        if (result is null || !result.Initialized)
        {
            return SftpOperationResult<SftpInitializeResponse>.Fail(
                SftpOperationStatus.CommandFailed,
                "SFTP initialization did not complete successfully.");
        }

        await _stateStore.SetInitializedAsync(true, cancellationToken);
        var state = await _stateStore.GetAsync(cancellationToken);

        return SftpOperationResult<SftpInitializeResponse>.Ok(new SftpInitializeResponse
        {
            Initialized = true,
            InitializedAtUtc = state.InitializedAtUtc
        });
    }

    public async Task<SftpOperationResult<SftpStatusResponse>> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        if (!await _authorizationService.AuthorizeAsync(cancellationToken))
        {
            return SftpOperationResult<SftpStatusResponse>.Fail(SftpOperationStatus.Forbidden, "SFTP operation is not authorized.");
        }

        var state = await _stateStore.GetAsync(cancellationToken);
        var initialized = state.Initialized;

        if (_options.EnableSftp && RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var response = await InvokeHelperAsync(
                PrivilegedHelperActions.SftpStatus,
                BuildInitializePayload(),
                cancellationToken);
            if (response.Success)
            {
                var helperStatus = response.Data.Deserialize<SftpStatusResultData>(PrivilegedHelperJson.SerializerOptions);
                initialized = helperStatus?.Initialized ?? initialized;
            }
        }

        return SftpOperationResult<SftpStatusResponse>.Ok(new SftpStatusResponse
        {
            Enabled = _options.EnableSftp,
            Initialized = initialized,
            Port = _options.SftpPort,
            Host = ResolveHost(),
            ActiveUserCount = state.Users.Count(user => user.Status == SftpUserStatus.Active),
            DisabledUserCount = state.Users.Count(user => user.Status == SftpUserStatus.Disabled),
            ExpiredUserCount = state.Users.Count(user => user.Status == SftpUserStatus.Expired)
        });
    }

    public async Task<SftpOperationResult<SftpUserCredentialsResponse>> CreateUserAsync(
        CreateSftpUserRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!await _authorizationService.AuthorizeAsync(cancellationToken))
        {
            return SftpOperationResult<SftpUserCredentialsResponse>.Fail(SftpOperationStatus.Forbidden, "SFTP operation is not authorized.");
        }

        if (!_options.EnableSftp)
        {
            return SftpOperationResult<SftpUserCredentialsResponse>.Fail(SftpOperationStatus.ServiceUnavailable, "SFTP is disabled.");
        }

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return SftpOperationResult<SftpUserCredentialsResponse>.Fail(SftpOperationStatus.NotSupported, "SFTP is only supported on Linux.");
        }

        if (!Guid.TryParse(request.JobId, out var jobId) || jobId == Guid.Empty)
        {
            return SftpOperationResult<SftpUserCredentialsResponse>.Fail(SftpOperationStatus.BadRequest, "jobId must be a valid GUID.");
        }

        if (!Enum.TryParse(request.JobType, ignoreCase: true, out JobDirectoryType jobType)
            || (jobType != JobDirectoryType.Export && jobType != JobDirectoryType.Import))
        {
            return SftpOperationResult<SftpUserCredentialsResponse>.Fail(SftpOperationStatus.BadRequest, "jobType must be Export or Import.");
        }

        if (jobType == JobDirectoryType.Import)
        {
            return SftpOperationResult<SftpUserCredentialsResponse>.Fail(
                SftpOperationStatus.NotImplemented,
                "Import SFTP user creation is not implemented yet.");
        }

        if (request.ExpiresAtUtc <= DateTime.UtcNow)
        {
            return SftpOperationResult<SftpUserCredentialsResponse>.Fail(
                SftpOperationStatus.BadRequest,
                "expiresAtUtc must be in the future.");
        }

        var state = await _stateStore.GetAsync(cancellationToken);
        if (!state.Initialized)
        {
            return SftpOperationResult<SftpUserCredentialsResponse>.Fail(
                SftpOperationStatus.ServiceUnavailable,
                "SFTP has not been initialized.");
        }

        if (!await _mountChecker.IsMountedAsync(_options.StorageRootPath, cancellationToken))
        {
            return SftpOperationResult<SftpUserCredentialsResponse>.Fail(
                SftpOperationStatus.ServiceUnavailable,
                "Storage is not mounted.");
        }

        var username = SftpUsernameGenerator.Generate(jobId, jobType);
        var existingByJob = await _stateStore.FindByJobAsync(jobId, jobType, cancellationToken);
        if (existingByJob is not null && !string.Equals(existingByJob.Username, username, StringComparison.Ordinal))
        {
            return SftpOperationResult<SftpUserCredentialsResponse>.Fail(
                SftpOperationStatus.Conflict,
                "Another SFTP user is already bound to this job.");
        }

        var existingByUsername = await _stateStore.FindByUsernameAsync(username, cancellationToken);
        if (existingByUsername is not null && existingByUsername.Status != SftpUserStatus.Deleted)
        {
            if (string.Equals(existingByUsername.JobId, jobId.ToString("D"), StringComparison.OrdinalIgnoreCase))
            {
                return SftpOperationResult<SftpUserCredentialsResponse>.Fail(
                    SftpOperationStatus.Conflict,
                    "SFTP user already exists for this job. Retrieve status instead of creating a new password.");
            }

            return SftpOperationResult<SftpUserCredentialsResponse>.Fail(
                SftpOperationStatus.Conflict,
                "SFTP username already exists for a different job.");
        }

        if (!_jobDirectoryProvider.TryResolveJobDirectory(jobId, jobType, out _, out var jobPath, out var pathError))
        {
            return SftpOperationResult<SftpUserCredentialsResponse>.Fail(
                SftpOperationStatus.BadRequest,
                pathError ?? "Job directory path could not be resolved.");
        }

        if (!Directory.Exists(jobPath))
        {
            return SftpOperationResult<SftpUserCredentialsResponse>.Fail(
                SftpOperationStatus.BadRequest,
                "Job directory does not exist.");
        }

        var markerPath = Path.Combine(jobPath, LinuxJobDirectoryProvider.MarkerFileName);
        if (!File.Exists(markerPath))
        {
            return SftpOperationResult<SftpUserCredentialsResponse>.Fail(
                SftpOperationStatus.Conflict,
                "Job directory marker is missing.");
        }

        var password = SftpPasswordGenerator.Generate();
        var chrootPath = Path.Combine(_options.SftpChrootBasePath, username);
        var filesMountPath = Path.Combine(chrootPath, "files");

        var helperResponse = await InvokeHelperAsync(
            PrivilegedHelperActions.SftpUserCreate,
            new SftpUserCreatePayload
            {
                Username = username,
                PlaintextPassword = password,
                JobType = MapJobType(jobType),
                JobId = jobId.ToString("D"),
                StorageRootPath = _options.StorageRootPath,
                JobPath = jobPath,
                ChrootBasePath = _options.SftpChrootBasePath,
                ExportGroupName = _options.SftpExportGroupName,
                ImportGroupName = _options.SftpImportGroupName,
                ExpiresAtUtc = request.ExpiresAtUtc,
                MarkerFileName = LinuxJobDirectoryProvider.MarkerFileName
            },
            cancellationToken);

        if (!helperResponse.Success)
        {
            return MapHelperFailure<SftpUserCredentialsResponse>(helperResponse);
        }

        var createResult = helperResponse.Data.Deserialize<SftpUserCreateResultData>(PrivilegedHelperJson.SerializerOptions);
        if (createResult is null)
        {
            return SftpOperationResult<SftpUserCredentialsResponse>.Fail(
                SftpOperationStatus.CommandFailed,
                "Privileged helper returned an invalid create payload.");
        }

        var record = new SftpUserRecord
        {
            Username = username,
            JobId = jobId.ToString("D"),
            JobType = jobType,
            ChrootPath = chrootPath,
            FilesMountPath = filesMountPath,
            JobPath = jobPath,
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = request.ExpiresAtUtc,
            Status = SftpUserStatus.Active,
            BindMountStatus = SftpBindMountStatus.Unknown
        };

        await _stateStore.UpsertUserAsync(record, cancellationToken);
        await RefreshBindMountStatusAsync(record, cancellationToken);

        _logger.LogInformation("Created SFTP user {Username} for {JobType} job {JobId}", username, jobType, jobId);

        return SftpOperationResult<SftpUserCredentialsResponse>.Ok(new SftpUserCredentialsResponse
        {
            Username = username,
            Password = password,
            Host = ResolveHost(),
            Port = _options.SftpPort,
            RemotePath = "/files",
            AccessType = nameof(SftpAccessType.DownloadOnly),
            ExpiresAtUtc = request.ExpiresAtUtc
        });
    }

    public async Task<SftpOperationResult<SftpUserInfoResponse>> GetUserAsync(
        string username,
        CancellationToken cancellationToken = default)
    {
        if (!await _authorizationService.AuthorizeAsync(cancellationToken))
        {
            return SftpOperationResult<SftpUserInfoResponse>.Fail(SftpOperationStatus.Forbidden, "SFTP operation is not authorized.");
        }

        var record = await _stateStore.FindByUsernameAsync(username, cancellationToken);
        if (record is null || record.Status == SftpUserStatus.Deleted)
        {
            return SftpOperationResult<SftpUserInfoResponse>.Fail(SftpOperationStatus.NotFound, "SFTP user was not found.");
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            await RefreshBindMountStatusAsync(record, cancellationToken);
        }

        return SftpOperationResult<SftpUserInfoResponse>.Ok(MapUserInfo(record));
    }

    private async Task RefreshBindMountStatusAsync(SftpUserRecord record, CancellationToken cancellationToken)
    {
        var statusResponse = await InvokeHelperAsync(
            PrivilegedHelperActions.SftpUserStatus,
            new SftpUserStatusPayload
            {
                Username = record.Username,
                ChrootPath = record.ChrootPath,
                FilesMountPath = record.FilesMountPath,
                JobPath = record.JobPath,
                ExportGroupName = _options.SftpExportGroupName,
                EnsureBindMount = record.Status == SftpUserStatus.Active
            },
            cancellationToken);

        if (!statusResponse.Success)
        {
            record.BindMountStatus = SftpBindMountStatus.Failed;
            record.BindMountMessage = BuildBindMountMessage(statusResponse);
            await _stateStore.UpsertUserAsync(record, cancellationToken);
            _logger.LogWarning(
                "Failed to refresh SFTP filesystem state for {Username}: {Message} (step={FailedStep}, stderr={Stderr})",
                record.Username,
                statusResponse.Message,
                statusResponse.FailedStep,
                statusResponse.FailedCommandStderr);
            return;
        }

        var helperStatus = statusResponse.Data.Deserialize<SftpUserStatusResultData>(PrivilegedHelperJson.SerializerOptions);
        var helperReportsActive = helperStatus?.BindMountActive == true;
        var hostVerifiedActive = RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
            && LinuxBindMountVerifier.IsBindMountActive(record.FilesMountPath, record.JobPath);

        if (helperReportsActive && !hostVerifiedActive)
        {
            record.BindMountStatus = SftpBindMountStatus.Failed;
            record.BindMountMessage = "SFTP bind mount was not detected on the host after helper completed.";
            await _stateStore.UpsertUserAsync(record, cancellationToken);
            _logger.LogWarning(
                "SFTP bind mount verification failed for {Username}: helper reported active but host mountinfo did not match {JobPath}.",
                record.Username,
                record.JobPath);
            return;
        }

        record.BindMountStatus = hostVerifiedActive
            ? SftpBindMountStatus.Active
            : helperReportsActive
                ? SftpBindMountStatus.Inactive
                : SftpBindMountStatus.Inactive;
        record.BindMountMessage = hostVerifiedActive
            ? null
            : helperReportsActive
                ? "SFTP bind mount is not active."
                : statusResponse.Message ?? "SFTP bind mount is not active.";
        await _stateStore.UpsertUserAsync(record, cancellationToken);
    }

    private static string BuildBindMountMessage(PrivilegedHelperResponse response)
    {
        if (!string.IsNullOrWhiteSpace(response.FailedCommandStderr))
        {
            return $"{response.Message} ({response.FailedCommandStderr})";
        }

        return response.Message ?? "SFTP bind mount refresh failed.";
    }

    public async Task<SftpOperationResult<SftpDisableUserResponse>> DisableUserAsync(
        string username,
        CancellationToken cancellationToken = default)
    {
        if (!await _authorizationService.AuthorizeAsync(cancellationToken))
        {
            return SftpOperationResult<SftpDisableUserResponse>.Fail(SftpOperationStatus.Forbidden, "SFTP operation is not authorized.");
        }

        var record = await _stateStore.FindByUsernameAsync(username, cancellationToken);
        if (record is null || record.Status == SftpUserStatus.Deleted)
        {
            return SftpOperationResult<SftpDisableUserResponse>.Fail(SftpOperationStatus.NotFound, "SFTP user was not found.");
        }

        var helperResponse = await InvokeHelperAsync(
            PrivilegedHelperActions.SftpUserDisable,
            new SftpUserDisablePayload
            {
                Username = record.Username,
                ChrootPath = record.ChrootPath
            },
            cancellationToken);
        if (!helperResponse.Success)
        {
            return MapHelperFailure<SftpDisableUserResponse>(helperResponse);
        }

        record.Status = SftpUserStatus.Disabled;
        record.DisabledAtUtc = DateTime.UtcNow;
        await _stateStore.UpsertUserAsync(record, cancellationToken);

        return SftpOperationResult<SftpDisableUserResponse>.Ok(new SftpDisableUserResponse
        {
            Username = record.Username,
            Status = record.Status,
            PasswordLocked = true
        });
    }

    public async Task<SftpOperationResult<SftpDeleteUserResponse>> DeleteUserAsync(
        string username,
        CancellationToken cancellationToken = default)
    {
        if (!await _authorizationService.AuthorizeAsync(cancellationToken))
        {
            return SftpOperationResult<SftpDeleteUserResponse>.Fail(SftpOperationStatus.Forbidden, "SFTP operation is not authorized.");
        }

        var record = await _stateStore.FindByUsernameAsync(username, cancellationToken);
        if (record is null || record.Status == SftpUserStatus.Deleted)
        {
            return SftpOperationResult<SftpDeleteUserResponse>.Fail(SftpOperationStatus.NotFound, "SFTP user was not found.");
        }

        var helperResponse = await InvokeHelperAsync(
            PrivilegedHelperActions.SftpUserDelete,
            new SftpUserDeletePayload
            {
                Username = record.Username,
                ChrootPath = record.ChrootPath,
                FilesMountPath = record.FilesMountPath,
                JobPath = record.JobPath
            },
            cancellationToken);
        if (!helperResponse.Success)
        {
            return MapHelperFailure<SftpDeleteUserResponse>(helperResponse);
        }

        var deleteResult = helperResponse.Data.Deserialize<SftpUserDeleteResultData>(PrivilegedHelperJson.SerializerOptions);
        if (deleteResult is null || !deleteResult.UserDeleted)
        {
            return SftpOperationResult<SftpDeleteUserResponse>.Fail(
                SftpOperationStatus.CommandFailed,
                helperResponse.Message ?? "SFTP user deletion did not complete.");
        }

        await _stateStore.RemoveUserAsync(record.Username, cancellationToken);

        return SftpOperationResult<SftpDeleteUserResponse>.Ok(new SftpDeleteUserResponse
        {
            Username = record.Username,
            Deleted = true,
            JobPathPreserved = deleteResult?.JobPathPreserved ?? true,
            AuditSteps = deleteResult?.AuditSteps ?? Array.Empty<string>()
        });
    }

    public async Task ReconcileExpiredUsersAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.EnableSftp)
        {
            return;
        }

        var state = await _stateStore.GetAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var gracePeriod = TimeSpan.FromMinutes(_options.SftpDeletionGracePeriodMinutes);

        foreach (var userRecord in state.Users.Where(user => user.Status != SftpUserStatus.Deleted).ToList())
        {
            var record = userRecord;
            if (record.ExpiresAtUtc > now)
            {
                continue;
            }

            if (record.Status == SftpUserStatus.Active)
            {
                _logger.LogInformation("Disabling expired SFTP user {Username}", record.Username);
                var disableResult = await DisableUserAsync(record.Username, cancellationToken);
                if (disableResult.Status != SftpOperationStatus.Success)
                {
                    _logger.LogWarning("Failed to disable expired SFTP user {Username}: {Message}", record.Username, disableResult.Message);
                    record.Status = SftpUserStatus.Expired;
                    await _stateStore.UpsertUserAsync(record, cancellationToken);
                    continue;
                }

                record = await _stateStore.FindByUsernameAsync(record.Username, cancellationToken) ?? record;
            }

            var disabledAt = record.DisabledAtUtc ?? record.ExpiresAtUtc;
            if (gracePeriod > TimeSpan.Zero && now < disabledAt + gracePeriod)
            {
                continue;
            }

            _logger.LogInformation("Deleting expired SFTP user {Username}", record.Username);
            var deleteResult = await DeleteUserAsync(record.Username, cancellationToken);
            if (deleteResult.Status != SftpOperationStatus.Success)
            {
                _logger.LogWarning("Failed to delete expired SFTP user {Username}: {Message}", record.Username, deleteResult.Message);
            }
        }
    }

    public async Task ReconcileBindMountsAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.EnableSftp || !RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return;
        }

        var state = await _stateStore.GetAsync(cancellationToken);
        foreach (var userRecord in state.Users.Where(user => user.Status == SftpUserStatus.Active).ToList())
        {
            await RefreshBindMountStatusAsync(userRecord, cancellationToken);
        }
    }

    private SftpInitializePayload BuildInitializePayload() => new()
    {
        ChrootBasePath = _options.SftpChrootBasePath,
        ExportGroupName = _options.SftpExportGroupName,
        ImportGroupName = _options.SftpImportGroupName,
        SshdDropInPath = _options.SftpSshdDropInPath
    };

    private string ResolveHost()
    {
        var setupHost = TryReadSetupUncHost();
        if (!string.IsNullOrWhiteSpace(setupHost))
        {
            return setupHost;
        }

        var fqdn = TryReadFqdn();
        if (!string.IsNullOrWhiteSpace(fqdn))
        {
            return fqdn;
        }

        return string.IsNullOrWhiteSpace(_options.PublicHostName)
            ? Environment.MachineName
            : _options.PublicHostName.Trim();
    }

    private string? TryReadSetupUncHost()
    {
        try
        {
            var config = _setupConfigStore.GetAsync().GetAwaiter().GetResult();
            return string.IsNullOrWhiteSpace(config.Samba?.UncHost) ? null : config.Samba.UncHost.Trim();
        }
        catch
        {
            return null;
        }
    }

    private static string? TryReadFqdn()
    {
        try
        {
            return System.Net.Dns.GetHostEntry(Environment.MachineName).HostName;
        }
        catch
        {
            return null;
        }
    }

    private SftpUserInfoResponse MapUserInfo(SftpUserRecord record) => new()
    {
        Username = record.Username,
        JobId = record.JobId,
        JobType = record.JobType,
        Host = ResolveHost(),
        Port = _options.SftpPort,
        RemotePath = "/files",
        AccessType = record.JobType == JobDirectoryType.Export
            ? nameof(SftpAccessType.DownloadOnly)
            : nameof(SftpAccessType.UploadOnly),
        CreatedAtUtc = record.CreatedAtUtc,
        ExpiresAtUtc = record.ExpiresAtUtc,
        Status = record.Status,
        BindMountStatus = record.BindMountStatus,
        BindMountMessage = record.BindMountMessage
    };

    private static SftpJobType MapJobType(JobDirectoryType jobType) =>
        jobType switch
        {
            JobDirectoryType.Export => SftpJobType.Export,
            JobDirectoryType.Import => SftpJobType.Import,
            _ => throw new ArgumentOutOfRangeException(nameof(jobType), jobType, "Unsupported job type.")
        };

    private Task<PrivilegedHelperResponse> InvokeHelperAsync(
        string action,
        object payload,
        CancellationToken cancellationToken)
    {
        var request = new PrivilegedHelperRequest
        {
            RequestId = Guid.NewGuid().ToString("D"),
            Action = action,
            Payload = JsonSerializer.SerializeToElement(payload, PrivilegedHelperJson.SerializerOptions)
        };

        return _privilegedHelperClient.InvokeAsync(request, cancellationToken);
    }

    private static SftpOperationResult<T> MapHelperFailure<T>(PrivilegedHelperResponse response)
    {
        var status = response.ErrorCode switch
        {
            PrivilegedHelperErrorCodes.InvalidArguments => SftpOperationStatus.BadRequest,
            PrivilegedHelperErrorCodes.InvalidUsername => SftpOperationStatus.BadRequest,
            PrivilegedHelperErrorCodes.InvalidJobPath => SftpOperationStatus.BadRequest,
            PrivilegedHelperErrorCodes.JobMarkerMissing => SftpOperationStatus.Conflict,
            PrivilegedHelperErrorCodes.JobMarkerMismatch => SftpOperationStatus.Conflict,
            PrivilegedHelperErrorCodes.UserExists => SftpOperationStatus.Conflict,
            PrivilegedHelperErrorCodes.UserNotFound => SftpOperationStatus.NotFound,
            PrivilegedHelperErrorCodes.ActiveSessionConflict => SftpOperationStatus.Conflict,
            PrivilegedHelperErrorCodes.BindMountConflict => SftpOperationStatus.Conflict,
            PrivilegedHelperErrorCodes.SftpNotImplemented => SftpOperationStatus.NotImplemented,
            PrivilegedHelperErrorCodes.SftpNotInitialized => SftpOperationStatus.ServiceUnavailable,
            _ => SftpOperationStatus.CommandFailed
        };

        return SftpOperationResult<T>.Fail(status, response.Message);
    }
}
