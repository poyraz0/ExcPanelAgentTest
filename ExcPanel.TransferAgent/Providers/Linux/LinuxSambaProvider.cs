using System.Runtime.InteropServices;
using System.Text.Json;
using ExcPanel.TransferAgent.Contracts;
using ExcPanel.TransferAgent.Models;
using ExcPanel.TransferAgent.Options;
using ExcPanel.TransferAgent.Providers.Linux;
using ExcPanel.TransferAgent.Services.Samba;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExcPanel.TransferAgent.Providers.Linux;

public class LinuxSambaProvider : ISambaProvider
{
    private readonly SambaOptions _options;
    private readonly TransferAgentOptions _agentOptions;
    private readonly SambaPathService _pathService;
    private readonly SambaProbeService _probeService;
    private readonly IStorageMountChecker _mountChecker;
    private readonly IJobDirectoryProvider _jobDirectoryProvider;
    private readonly IPrivilegedHelperClient _privilegedHelperClient;
    private readonly ILogger<LinuxSambaProvider> _logger;

    public LinuxSambaProvider(
        IOptions<SambaOptions> options,
        IOptions<TransferAgentOptions> agentOptions,
        SambaPathService pathService,
        SambaProbeService probeService,
        IStorageMountChecker mountChecker,
        IJobDirectoryProvider jobDirectoryProvider,
        IPrivilegedHelperClient privilegedHelperClient,
        ILogger<LinuxSambaProvider> logger)
    {
        _options = options.Value;
        _agentOptions = agentOptions.Value;
        _pathService = pathService;
        _probeService = probeService;
        _mountChecker = mountChecker;
        _jobDirectoryProvider = jobDirectoryProvider;
        _privilegedHelperClient = privilegedHelperClient;
        _logger = logger;
    }

    public async Task<SambaStatusResponse> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var validation = await ValidateConfigurationAsync(cancellationToken);
        var serverName = _pathService.ResolveServerName();
        var storageRoot = _pathService.ResolveStorageRoot();
        var shareName = _pathService.ResolveShareName();
        var uncRoot = _pathService.BuildUncRoot();

        var sambaInstalled = await _probeService.IsPackageInstalledAsync("samba", cancellationToken);
        var smbdRunning = await _probeService.IsServiceActiveAsync("smbd", cancellationToken);
        var winbindInstalled = await _probeService.IsPackageInstalledAsync("winbind", cancellationToken);
        var winbindRunning = await _probeService.IsServiceActiveAsync("winbind", cancellationToken);
        var (domainJoined, joinedDomain, _) = await _probeService.GetDomainJoinStatusAsync(cancellationToken);
        var groupResolved = await _probeService.IsAdGroupResolvedAsync(_options.RequiredAdGroup, cancellationToken);
        var shareConfigured = await IsShareConfiguredAsync(cancellationToken);
        var shareReachable = shareConfigured && await TestShareAsync(cancellationToken);

        return new SambaStatusResponse
        {
            Enabled = _options.Enabled,
            Supported = true,
            Platform = "Linux",
            SambaInstalled = sambaInstalled,
            SmbdRunning = smbdRunning,
            WinbindInstalled = winbindInstalled,
            WinbindRunning = winbindRunning,
            DomainJoined = domainJoined,
            JoinedDomain = joinedDomain,
            ShareConfigured = shareConfigured,
            ShareReachableLocally = shareReachable,
            ShareName = shareName,
            StorageRoot = storageRoot,
            ServerName = serverName,
            UncRoot = uncRoot,
            RequiredAdGroup = _options.RequiredAdGroup,
            RequiredAdGroupResolved = groupResolved,
            ConfigurationValid = validation.Valid,
            ValidationErrors = validation.Errors,
            Warnings = validation.Warnings
        };
    }

    public async Task<SambaPrerequisitesResponse> GetPrerequisitesAsync(CancellationToken cancellationToken = default)
    {
        var storageMounted = await _mountChecker.IsMountedAsync(_agentOptions.StorageRootPath, cancellationToken);
        var items = await _probeService.BuildPrerequisiteItemsAsync(storageMounted, cancellationToken);
        var optionErrors = _pathService.ValidateOptions();

        if (optionErrors.Count > 0)
        {
            items = items.Concat(optionErrors.Select(error => new SambaPrerequisiteItem
            {
                Name = "samba-options",
                Satisfied = false,
                Message = error,
                RecommendedActions = ["Correct Samba configuration in appsettings."]
            })).ToList();
        }

        return new SambaPrerequisitesResponse
        {
            Supported = true,
            Ready = items.All(item => item.Satisfied),
            Items = items
        };
    }

    public async Task<SambaInitializeDryRunResponse> BuildConfigurationPlanAsync(CancellationToken cancellationToken = default)
    {
        var validation = await ValidateConfigurationAsync(cancellationToken);
        var prerequisites = await GetPrerequisitesAsync(cancellationToken);

        var plannedActions = new List<string>
        {
            "Verify required Samba packages are installed",
            $"Verify storage root exists and is mounted at {_pathService.ResolveStorageRoot()}",
            "Verify domain membership when RequireDomainJoined is enabled",
            $"Verify AD group '{_options.RequiredAdGroup}' can be resolved",
            $"Create timestamped backups of {_options.ConfigFilePath} and {_options.MainConfigPath}",
            $"Write managed Samba share configuration to {_options.ConfigFilePath}",
            $"Ensure include line exists in {_options.MainConfigPath}",
            "Apply ACL inheritance on storage root for domain group access",
            "Validate configuration with testparm -s",
            "Reload smbd service without restart",
            "Verify share is listed in effective Samba configuration"
        };

        var warnings = validation.Warnings.ToList();
        if (!prerequisites.Ready)
        {
            warnings.Add("One or more prerequisites are not satisfied.");
        }

        return new SambaInitializeDryRunResponse
        {
            Supported = true,
            Valid = validation.Valid && prerequisites.Ready,
            PlannedActions = plannedActions,
            ValidationErrors = validation.Errors.Concat(
                    prerequisites.Items.Where(i => !i.Satisfied).Select(i => i.Message ?? i.Name))
                .Distinct(StringComparer.Ordinal)
                .ToList(),
            Warnings = warnings
        };
    }

    public async Task<SambaOperationResult<SambaInitializeResponse>> InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return SambaOperationResult<SambaInitializeResponse>.Fail(
                SambaOperationStatus.NotSupported,
                "Samba initialization is only supported on Linux.");
        }

        if (!_options.Enabled)
        {
            return SambaOperationResult<SambaInitializeResponse>.Fail(
                SambaOperationStatus.ServiceUnavailable,
                "Samba is disabled.");
        }

        var dryRun = await BuildConfigurationPlanAsync(cancellationToken);
        if (!dryRun.Valid)
        {
            var message = dryRun.ValidationErrors.FirstOrDefault() ?? "Samba initialization validation failed.";
            return SambaOperationResult<SambaInitializeResponse>.Fail(
                SambaOperationStatus.ValidationFailed,
                message);
        }

        var steps = new List<SambaInitializeStepResult>();

        if (!await _mountChecker.IsMountedAsync(_agentOptions.StorageRootPath, cancellationToken))
        {
            return SambaOperationResult<SambaInitializeResponse>.Fail(
                SambaOperationStatus.ServiceUnavailable,
                "Storage is not mounted.");
        }

        if (_options.RequireDomainJoined)
        {
            var (joined, _, joinError) = await _probeService.GetDomainJoinStatusAsync(cancellationToken);
            if (!joined)
            {
                return SambaOperationResult<SambaInitializeResponse>.Fail(
                    SambaOperationStatus.ValidationFailed,
                    string.IsNullOrWhiteSpace(joinError)
                        ? "Server is not joined to Active Directory."
                        : joinError);
            }
        }

        if (!await _probeService.IsAdGroupResolvedAsync(_options.RequiredAdGroup, cancellationToken))
        {
            return SambaOperationResult<SambaInitializeResponse>.Fail(
                SambaOperationStatus.ValidationFailed,
                $"Required AD group '{_options.RequiredAdGroup}' could not be resolved.");
        }

        var helperRequest = new PrivilegedHelperRequest
        {
            RequestId = Guid.NewGuid().ToString("D"),
            Action = PrivilegedHelperActions.SambaConfigure,
            Payload = JsonSerializer.SerializeToElement(BuildConfigurePayload(), PrivilegedHelperJson.SerializerOptions)
        };

        var helperResponse = await _privilegedHelperClient.InvokeAsync(helperRequest, cancellationToken);
        foreach (var completedStep in helperResponse.CompletedSteps)
        {
            steps.Add(new SambaInitializeStepResult
            {
                Step = completedStep,
                Success = true
            });
        }

        if (!helperResponse.Success)
        {
            if (!string.IsNullOrWhiteSpace(helperResponse.FailedStep))
            {
                steps.Add(new SambaInitializeStepResult
                {
                    Step = helperResponse.FailedStep,
                    Success = false,
                    Message = helperResponse.Message
                });
            }

            return SambaOperationResult<SambaInitializeResponse>.Fail(
                MapHelperFailureStatus(helperResponse.ErrorCode),
                helperResponse.Message);
        }

        var resultData = helperResponse.Data.Deserialize<SambaConfigureResultData>(PrivilegedHelperJson.SerializerOptions);
        var status = await GetStatusAsync(cancellationToken);

        return SambaOperationResult<SambaInitializeResponse>.Ok(new SambaInitializeResponse
        {
            Initialized = resultData?.Configured ?? true,
            ShareConfigured = resultData?.ShareConfigured ?? status.ShareConfigured,
            ConfigurationValid = resultData?.ConfigurationValid ?? status.ConfigurationValid,
            ShareReachableLocally = resultData?.ShareReachableLocally ?? status.ShareReachableLocally,
            ShareName = status.ShareName,
            StorageRoot = status.StorageRoot,
            ServerName = status.ServerName,
            UncRoot = status.UncRoot,
            Steps = steps,
            ValidationErrors = status.ValidationErrors
        }, "Samba initialized successfully.");
    }

    public async Task<(bool Valid, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings)> ValidateConfigurationAsync(
        CancellationToken cancellationToken = default)
    {
        var errors = _pathService.ValidateOptions().ToList();
        var warnings = new List<string>();

        if (_options.RequireDomainJoined)
        {
            var (joined, _, joinError) = await _probeService.GetDomainJoinStatusAsync(cancellationToken);
            if (!joined)
            {
                errors.Add("Server is not joined to Active Directory.");
                if (!string.IsNullOrWhiteSpace(joinError))
                {
                    errors.Add(joinError.Trim());
                }
            }
        }

        if (!await _probeService.IsAdGroupResolvedAsync(_options.RequiredAdGroup, cancellationToken))
        {
            errors.Add($"Required AD group '{_options.RequiredAdGroup}' could not be resolved.");
        }

        if (!await _mountChecker.IsMountedAsync(_agentOptions.StorageRootPath, cancellationToken))
        {
            errors.Add("Storage root is not mounted.");
        }

        if (!await _probeService.IsPackageInstalledAsync("samba", cancellationToken))
        {
            warnings.Add("Samba package is not installed.");
        }

        return (errors.Count == 0, errors, warnings);
    }

    public SambaOperationResult<SambaUncPathResponse> BuildUncPath(Guid jobId, JobDirectoryType jobType)
    {
        if (!_options.Enabled)
        {
            return SambaOperationResult<SambaUncPathResponse>.Fail(
                SambaOperationStatus.ServiceUnavailable,
                "Samba is disabled.");
        }

        if (!_jobDirectoryProvider.TryResolveJobDirectory(
                jobId,
                jobType,
                out var relativePath,
                out var physicalPath,
                out var pathError))
        {
            return SambaOperationResult<SambaUncPathResponse>.Fail(
                SambaOperationStatus.BadRequest,
                pathError ?? "Job directory path could not be resolved.");
        }

        var optionErrors = _pathService.ValidateOptions();
        if (optionErrors.Count > 0)
        {
            return SambaOperationResult<SambaUncPathResponse>.Fail(
                SambaOperationStatus.ValidationFailed,
                optionErrors[0]);
        }

        if (IsSymlinkOrReparsePoint(physicalPath))
        {
            return SambaOperationResult<SambaUncPathResponse>.Fail(
                SambaOperationStatus.Conflict,
                "Job directory path resolves through a symlink.");
        }

        var unc = _pathService.TryBuildUncPath(jobId, jobType, relativePath, physicalPath);
        if (unc is null)
        {
            return SambaOperationResult<SambaUncPathResponse>.Fail(
                SambaOperationStatus.ValidationFailed,
                "UNC path could not be built from current Samba configuration.");
        }

        return SambaOperationResult<SambaUncPathResponse>.Ok(unc);
    }

    public async Task<bool> TestShareAsync(CancellationToken cancellationToken = default)
    {
        if (!await _probeService.IsServiceActiveAsync("smbd", cancellationToken))
        {
            return false;
        }

        return await _probeService.IsShareListedInTestparmAsync(_pathService.ResolveShareName(), cancellationToken);
    }

    private async Task<bool> IsShareConfiguredAsync(CancellationToken cancellationToken) =>
        await _probeService.IsShareListedInTestparmAsync(_pathService.ResolveShareName(), cancellationToken);

    private SambaConfigurePayload BuildConfigurePayload() => new()
    {
        ShareName = _pathService.ResolveShareName(),
        StorageRoot = _pathService.ResolveStorageRoot(),
        ConfigFilePath = _options.ConfigFilePath,
        MainConfigPath = _options.MainConfigPath,
        RequiredAdGroup = _options.RequiredAdGroup,
        AllowGuest = _options.AllowGuest,
        SmbMinProtocol = _options.SmbMinProtocol,
        SmbMaxProtocol = _options.SmbMaxProtocol,
        OwnerUser = _agentOptions.StorageOwnerUser,
        OwnerGroup = _agentOptions.StorageOwnerGroup
    };

    private static SambaOperationStatus MapHelperFailureStatus(string? errorCode) =>
        errorCode switch
        {
            PrivilegedHelperErrorCodes.DomainNotJoined => SambaOperationStatus.ValidationFailed,
            PrivilegedHelperErrorCodes.AdGroupNotResolved => SambaOperationStatus.ValidationFailed,
            PrivilegedHelperErrorCodes.StorageNotMounted => SambaOperationStatus.ServiceUnavailable,
            PrivilegedHelperErrorCodes.SambaValidationFailed => SambaOperationStatus.CommandFailed,
            PrivilegedHelperErrorCodes.InvalidArguments => SambaOperationStatus.BadRequest,
            _ => SambaOperationStatus.CommandFailed
        };

    private static bool IsSymlinkOrReparsePoint(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Attributes.HasFlag(FileAttributes.ReparsePoint);
        }
        catch
        {
            return false;
        }
    }
}
