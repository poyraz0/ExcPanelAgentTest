using System.Runtime.InteropServices;
using ExcPanel.TransferAgent.Contracts;
using ExcPanel.TransferAgent.Models;
using ExcPanel.TransferAgent.Models.Domain;
using ExcPanel.TransferAgent.Models.Export;
using ExcPanel.TransferAgent.Models.Setup;
using ExcPanel.TransferAgent.Options;
using ExcPanel.TransferAgent.Services.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExcPanel.TransferAgent.Services.Setup;

public class SetupOrchestrationService : ISetupOrchestrationService
{
    private readonly ISetupStateStore _stateStore;
    private readonly ISetupPrerequisitesService _prerequisitesService;
    private readonly IStorageDiskDiscoveryService _diskDiscoveryService;
    private readonly IStorageConfigureService _storageConfigureService;
    private readonly IStorageMountChecker _mountChecker;
    private readonly IStorageMountRecoveryService _mountRecovery;
    private readonly ISetupConfigStore _setupConfigStore;
    private readonly IStorageService _storageService;
    private readonly IDomainService _domainService;
    private readonly ISambaProvider _sambaProvider;
    private readonly ISftpService _sftpService;
    private readonly IJobDirectoryProvider _jobDirectoryProvider;
    private readonly IExportPrepareService _exportPrepareService;
    private readonly TransferAgentOptions _agentOptions;
    private readonly SetupOptions _setupOptions;
    private readonly SambaOptions _sambaOptions;
    private readonly IConfirmationTokenService _confirmationTokenService;
    private readonly ILogger<SetupOrchestrationService> _logger;

    public SetupOrchestrationService(
        ISetupStateStore stateStore,
        ISetupPrerequisitesService prerequisitesService,
        IStorageDiskDiscoveryService diskDiscoveryService,
        IStorageConfigureService storageConfigureService,
        IStorageMountChecker mountChecker,
        IStorageMountRecoveryService mountRecovery,
        ISetupConfigStore setupConfigStore,
        IStorageService storageService,
        IDomainService domainService,
        ISambaProvider sambaProvider,
        ISftpService sftpService,
        IJobDirectoryProvider jobDirectoryProvider,
        IExportPrepareService exportPrepareService,
        IOptions<TransferAgentOptions> agentOptions,
        IOptions<SetupOptions> setupOptions,
        IOptions<SambaOptions> sambaOptions,
        IConfirmationTokenService confirmationTokenService,
        ILogger<SetupOrchestrationService> logger)
    {
        _stateStore = stateStore;
        _prerequisitesService = prerequisitesService;
        _diskDiscoveryService = diskDiscoveryService;
        _storageConfigureService = storageConfigureService;
        _mountChecker = mountChecker;
        _mountRecovery = mountRecovery;
        _setupConfigStore = setupConfigStore;
        _storageService = storageService;
        _domainService = domainService;
        _sambaProvider = sambaProvider;
        _sftpService = sftpService;
        _jobDirectoryProvider = jobDirectoryProvider;
        _exportPrepareService = exportPrepareService;
        _agentOptions = agentOptions.Value;
        _setupOptions = setupOptions.Value;
        _sambaOptions = sambaOptions.Value;
        _confirmationTokenService = confirmationTokenService;
        _logger = logger;
    }

    public async Task<SetupStatusResponse> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var state = await _stateStore.GetAsync(cancellationToken);
        return MapStatus(state);
    }

    public async Task<SetupPlanResponse> PlanAsync(SetupPlanRequest request, CancellationToken cancellationToken = default)
    {
        var response = new SetupPlanResponse();
        var validationErrors = new List<string>();

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            validationErrors.Add("Setup is only supported on Linux.");
        }

        var prerequisites = await _prerequisitesService.CheckAsync(cancellationToken);
        if (!prerequisites.Ready)
        {
            response.Warnings.Add("One or more prerequisites failed.");
        }

        var storageMounted = await ResolveStorageMountedAsync(
            request.Storage?.MountPath ?? _agentOptions.StorageRootPath,
            cancellationToken);

        var needsDiskConfigure = request.Storage is not null &&
            !string.IsNullOrWhiteSpace(request.Storage.DiskPath) &&
            !storageMounted;

        if (needsDiskConfigure && request.Storage is not null)
        {
            var dryRun = await _storageConfigureService.DryRunAsync(new StorageConfigureDryRunRequest
            {
                DiskPath = request.Storage.DiskPath,
                MountPath = request.Storage.MountPath
            }, cancellationToken);

            validationErrors.AddRange(dryRun.ValidationErrors);

            if (dryRun.Valid && dryRun.Disk is not null)
            {
                var confirmationCode = SetupConfirmationHelper.BuildDiskFormatConfirmationCode(dryRun.Disk);
                response.DestructiveActions.Add(new SetupDestructiveAction
                {
                    Code = "format-disk",
                    Description = $"Format disk {dryRun.Disk.Path} ({dryRun.Disk.SizeGb:F0} GB) and mount at {dryRun.TargetMountPath}. ALL DATA WILL BE LOST.",
                    ConfirmationCode = confirmationCode
                });
                response.RequiredConfirmationCodes.Add(confirmationCode);
            }
        }

        var sambaStatusForPlan = await _sambaProvider.GetStatusAsync(cancellationToken);
        var domainJoined = sambaStatusForPlan.DomainJoined;

        AddPlanStep(response, SetupStepNames.SystemPrerequisites, "Verify system prerequisites", true, false, null);
        AddPlanStep(response, SetupStepNames.DiskDiscovery, "Discover available disks", needsDiskConfigure, !needsDiskConfigure, storageMounted ? "Storage already mounted." : null);
        AddPlanStep(response, SetupStepNames.DiskConfigureDryRun, "Plan disk configuration", needsDiskConfigure, !needsDiskConfigure, storageMounted ? "Storage already mounted." : null);
        AddPlanStep(response, SetupStepNames.DiskConfigure, "Configure and mount storage", needsDiskConfigure, !needsDiskConfigure, storageMounted ? "Storage already mounted." : null);

        var needsDomain = request.Domain is not null && _setupOptions.RequireDomainJoinForSamba && !domainJoined;
        AddPlanStep(response, SetupStepNames.DomainPrecheck, "Verify DNS and domain controller", needsDomain, !needsDomain, domainJoined ? "Already joined to domain." : null);
        AddPlanStep(response, SetupStepNames.DomainJoin, "Join Active Directory domain", needsDomain, !needsDomain, domainJoined ? "Already joined to domain." : null);

        var needsSamba = request.Samba is not null && _setupOptions.RequireSambaForExchangeExport && !sambaStatusForPlan.ShareConfigured;
        AddPlanStep(response, SetupStepNames.SambaInitialize, "Initialize Samba share", needsSamba, !needsSamba, sambaStatusForPlan.ShareConfigured ? "Samba share already configured." : null);

        var sftpStatus = await _sftpService.GetStatusAsync(cancellationToken);
        var needsSftp = request.Sftp?.Enabled == true && _setupOptions.RequireSftpForUserDownload &&
                        sftpStatus.Status == SftpOperationStatus.Success &&
                        sftpStatus.Data is { Initialized: false };
        AddPlanStep(response, SetupStepNames.SftpInitialize, "Initialize SFTP", needsSftp, !needsSftp,
            sftpStatus.Data?.Initialized == true ? "SFTP already initialized." : null);

        AddPlanStep(response, SetupStepNames.ExportPathTest, "Create test export job directory and UNC path", true, false, null);
        AddPlanStep(response, SetupStepNames.SambaWriteTest, "Verify local write and Samba config", true, false, null);
        AddPlanStep(response, SetupStepNames.FinalSummary, "Produce setup summary", true, false, null);

        if (needsDiskConfigure && request.Storage is not null)
        {
            response.RequiredConfirmationCodes.Add("formatDisk confirmation required in apply request.");
        }

        response.ValidationErrors = validationErrors.Distinct(StringComparer.Ordinal).ToList();
        response.CanApply = validationErrors.Count == 0;
        return response;
    }

    public async Task<SetupApplyResponse> ApplyAsync(SetupApplyRequest request, CancellationToken cancellationToken = default)
    {
        var state = await _stateStore.GetAsync(cancellationToken);
        state.SetupId = string.IsNullOrWhiteSpace(state.SetupId) ? Guid.NewGuid().ToString("D") : state.SetupId;
        state.Status = SetupStatus.Running;
        state.StartedAtUtc ??= DateTime.UtcNow;
        state.LastError = null;
        await _stateStore.SaveAsync(state, cancellationToken);

        await PersistSetupConfigAsync(request, cancellationToken);

        var summary = new SetupSummary { StorageRootPath = _agentOptions.StorageRootPath };
        var warnings = new List<string>();

        try
        {
            await RunStepAsync(state, SetupStepNames.SystemPrerequisites, async () =>
            {
                var prereq = await _prerequisitesService.CheckAsync(cancellationToken);
                if (!prereq.Ready)
                {
                    throw new InvalidOperationException("System prerequisites are not satisfied.");
                }
            }, cancellationToken);

            var mountPath = request.Storage?.MountPath ?? _agentOptions.StorageRootPath;
            var storageMounted = await ResolveStorageMountedAsync(mountPath, cancellationToken);

            if (request.Storage is not null && !string.IsNullOrWhiteSpace(request.Storage.DiskPath) && !storageMounted)
            {
                if (await _mountRecovery.IsConfiguredAsync(mountPath, cancellationToken))
                {
                    throw new InvalidOperationException(
                        $"Storage is configured in fstab but could not be remounted at '{mountPath}'. Run: sudo mount {mountPath}");
                }

                await RunStepAsync(state, SetupStepNames.DiskDiscovery, async () =>
                {
                    var disks = await _diskDiscoveryService.DiscoverDisksAsync(cancellationToken);
                    if (!disks.Supported)
                    {
                        throw new InvalidOperationException(disks.Message ?? "Disk discovery not supported.");
                    }
                }, cancellationToken);

                StorageConfigureDryRunResponse? dryRun = null;
                await RunStepAsync(state, SetupStepNames.DiskConfigureDryRun, async () =>
                {
                    dryRun = await _storageConfigureService.DryRunAsync(new StorageConfigureDryRunRequest
                    {
                        DiskPath = request.Storage!.DiskPath,
                        MountPath = request.Storage.MountPath
                    }, cancellationToken);

                    if (!dryRun.Valid || dryRun.Disk is null)
                    {
                        throw new InvalidOperationException(dryRun.ValidationErrors.FirstOrDefault() ?? "Disk dry-run failed.");
                    }
                }, cancellationToken);

                await RunStepAsync(state, SetupStepNames.DiskConfigure, async () =>
                {
                    if (dryRun?.Disk is null)
                    {
                        throw new InvalidOperationException("Disk dry-run result missing.");
                    }

                    var expectedCode = SetupConfirmationHelper.BuildDiskFormatConfirmationCode(dryRun.Disk);
                    if (!string.Equals(request.Confirmations?.FormatDisk, expectedCode, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException($"Disk format confirmation code mismatch. Expected '{expectedCode}'.");
                    }

                    var configureResult = await _storageConfigureService.ConfigureAsync(new ConfigureStorageRequest
                    {
                        DiskPath = request.Storage!.DiskPath,
                        MountPath = request.Storage.MountPath,
                        FileSystem = request.Storage.FileSystem,
                        ConfirmationCode = dryRun.ConfirmationCode ?? string.Empty
                    }, cancellationToken);

                    if (configureResult.Status != ConfigureStorageStatus.Success)
                    {
                        throw new InvalidOperationException(configureResult.Message ?? "Storage configure failed.");
                    }

                    summary.StorageConfigured = true;
                }, cancellationToken);
            }
            else
            {
                await SkipStepAsync(state, SetupStepNames.DiskDiscovery, "Storage already mounted or not requested.", cancellationToken);
                await SkipStepAsync(state, SetupStepNames.DiskConfigureDryRun, "Storage already mounted or not requested.", cancellationToken);
                await SkipStepAsync(state, SetupStepNames.DiskConfigure, storageMounted ? "Storage already mounted." : "Disk configure not requested.", cancellationToken);
                summary.StorageConfigured = storageMounted;
            }

            if (request.Domain is not null && _setupOptions.RequireDomainJoinForSamba)
            {
                await RunStepAsync(state, SetupStepNames.DomainPrecheck, async () =>
                {
                    var precheck = await _domainService.PrecheckAsync(new DomainPrecheckRequest
                    {
                        DnsDomain = request.Domain!.DnsDomain,
                        Realm = request.Domain.Realm,
                        Workgroup = request.Domain.Workgroup,
                        DomainController = request.Domain.DomainController,
                        DomainControllerIp = request.Domain.DomainControllerIp,
                        ComputerName = request.Domain.ComputerName,
                        RequiredAdGroup = request.Samba?.RequiredAdGroup ?? _sambaOptions.RequiredAdGroup
                    }, cancellationToken);

                    if (!precheck.Ready && !precheck.AlreadyJoined)
                    {
                        var failedChecks = precheck.Checks
                            .Where(c => !c.Passed)
                            .Select(c => $"{c.Name}: {c.Message ?? "failed"}")
                            .ToList();
                        var detail = failedChecks.Count > 0
                            ? string.Join("; ", failedChecks)
                            : "unknown reason";
                        throw new InvalidOperationException($"Domain precheck failed. {detail}");
                    }
                }, cancellationToken);

                await RunStepAsync(state, SetupStepNames.DomainJoin, async () =>
                {
                    var status = await _domainService.GetStatusAsync(cancellationToken);
                    if (status.DomainJoined)
                    {
                        return;
                    }

                    if (request.Confirmations?.JoinDomain != true)
                    {
                        throw new InvalidOperationException("Domain join confirmation is required.");
                    }

                    if (request.DomainCredentials is null || string.IsNullOrWhiteSpace(request.DomainCredentials.Password))
                    {
                        throw new InvalidOperationException("Domain credentials are required for join.");
                    }

                    var joinResult = await _domainService.JoinAsync(new DomainJoinRequest
                    {
                        DnsDomain = request.Domain!.DnsDomain,
                        Realm = request.Domain.Realm,
                        Workgroup = request.Domain.Workgroup,
                        DomainController = request.Domain.DomainController,
                        DomainControllerIp = request.Domain.DomainControllerIp,
                        Username = request.DomainCredentials.Username,
                        Password = request.DomainCredentials.Password,
                        ComputerName = request.Domain.ComputerName,
                        ComputerOu = request.Domain.ComputerOu,
                        RequiredAdGroup = request.Samba?.RequiredAdGroup ?? _sambaOptions.RequiredAdGroup
                    }, cancellationToken);

                    summary.DomainJoined = joinResult.Joined || joinResult.Skipped;
                }, cancellationToken);
            }
            else
            {
                await SkipStepAsync(state, SetupStepNames.DomainPrecheck, "Domain join not required.", cancellationToken);
                await SkipStepAsync(state, SetupStepNames.DomainJoin, "Domain join not required.", cancellationToken);
            }

            if (request.Samba is not null && _setupOptions.RequireSambaForExchangeExport)
            {
                await RunStepAsync(state, SetupStepNames.SambaInitialize, async () =>
                {
                    var sambaStatus = await _sambaProvider.GetStatusAsync(cancellationToken);
                    if (sambaStatus.ShareConfigured && sambaStatus.ConfigurationValid)
                    {
                        summary.SambaConfigured = true;
                        summary.UncRoot = sambaStatus.UncRoot;
                        return;
                    }

                    if (request.Confirmations?.ConfigureSamba != true)
                    {
                        throw new InvalidOperationException("Samba configure confirmation is required.");
                    }

                    var initResult = await _sambaProvider.InitializeAsync(cancellationToken);
                    if (initResult.Status != SambaOperationStatus.Success)
                    {
                        throw new InvalidOperationException(initResult.Message ?? "Samba initialize failed.");
                    }

                    if (!initResult.Data!.ShareReachableLocally)
                    {
                        warnings.Add("Samba share is not reachable locally; this may be expected when guest access is disabled.");
                    }

                    summary.SambaConfigured = true;
                    summary.UncRoot = initResult.Data.UncRoot;
                }, cancellationToken);
            }
            else
            {
                await SkipStepAsync(state, SetupStepNames.SambaInitialize, "Samba not required.", cancellationToken);
            }

            if (request.Sftp?.Enabled == true && _setupOptions.RequireSftpForUserDownload)
            {
                await RunStepAsync(state, SetupStepNames.SftpInitialize, async () =>
                {
                    var sftpStatus = await _sftpService.GetStatusAsync(cancellationToken);
                    if (sftpStatus.Status == SftpOperationStatus.Success && sftpStatus.Data!.Initialized)
                    {
                        summary.SftpInitialized = true;
                        return;
                    }

                    if (request.Confirmations?.InitializeSftp != true)
                    {
                        throw new InvalidOperationException("SFTP initialize confirmation is required.");
                    }

                    var initResult = await _sftpService.InitializeAsync(cancellationToken);
                    if (initResult.Status != SftpOperationStatus.Success)
                    {
                        throw new InvalidOperationException(initResult.Message ?? "SFTP initialize failed.");
                    }

                    summary.SftpInitialized = true;
                }, cancellationToken);
            }
            else
            {
                await SkipStepAsync(state, SetupStepNames.SftpInitialize, "SFTP not required.", cancellationToken);
            }

            Guid testJobId = Guid.Empty;
            await RunStepAsync(state, SetupStepNames.ExportPathTest, async () =>
            {
                testJobId = Guid.NewGuid();
                var exportResult = await _exportPrepareService.PrepareAsync(new ExportPrepareRequest
                {
                    JobId = testJobId.ToString("D"),
                    Mailbox = "setup-test@excpanel.local",
                    Domain = "excpanel.local"
                }, cancellationToken);

                if (string.IsNullOrWhiteSpace(exportResult.ExchangeFilePath))
                {
                    throw new InvalidOperationException("Export path test did not produce exchangeFilePath.");
                }
            }, cancellationToken);

            await RunStepAsync(state, SetupStepNames.SambaWriteTest, async () =>
            {
                var writeTest = await TestSambaWriteAsync(cancellationToken);
                if (!writeTest.LocalWriteSucceeded)
                {
                    throw new InvalidOperationException("Samba write test failed at filesystem level.");
                }

                warnings.AddRange(writeTest.Warnings);
            }, cancellationToken);

            await RunStepAsync(state, SetupStepNames.FinalSummary, () =>
            {
                summary.Warnings = warnings;
                return Task.CompletedTask;
            }, cancellationToken);

            state.Status = warnings.Count > 0 ? SetupStatus.PartiallyCompleted : SetupStatus.Completed;
            state.CompletedAtUtc = DateTime.UtcNow;
            state.CurrentStep = SetupStepNames.FinalSummary;
            await _stateStore.SaveAsync(state, cancellationToken);

            return new SetupApplyResponse
            {
                Status = state.Status,
                CurrentStep = state.CurrentStep,
                Steps = state.Steps,
                Summary = summary
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Setup apply failed at step {Step}", state.CurrentStep);
            state.Status = SetupStatus.Failed;
            state.LastError = ex.Message;
            state.CompletedAtUtc = DateTime.UtcNow;
            await _stateStore.SaveAsync(state, cancellationToken);

            return new SetupApplyResponse
            {
                Status = state.Status,
                CurrentStep = state.CurrentStep,
                Steps = state.Steps,
                LastError = ex.Message
            };
        }
    }

    public async Task<SetupSambaWriteTestResponse> TestSambaWriteAsync(CancellationToken cancellationToken = default)
    {
        var response = new SetupSambaWriteTestResponse();
        var warnings = new List<string>();
        var errors = new List<string>();

        var sambaStatus = await _sambaProvider.GetStatusAsync(cancellationToken);
        response.ShareConfigured = sambaStatus.ShareConfigured;

        if (!sambaStatus.ShareConfigured)
        {
            errors.Add("Samba share is not configured.");
        }

        if (!sambaStatus.ShareReachableLocally && sambaStatus.ConfigurationValid)
        {
            warnings.Add("Share is not reachable locally; guest access is disabled and anonymous local test may fail.");
        }

        response.AclPresent = sambaStatus.RequiredAdGroupResolved;

        var testJobId = Guid.NewGuid();
        var createResult = await _jobDirectoryProvider.CreateAsync(testJobId, JobDirectoryType.Export, cancellationToken);
        if (createResult.Status != JobDirectoryOperationStatus.Success || createResult.Data is null)
        {
            errors.Add(createResult.Message ?? "Failed to create test job directory.");
            response.Errors = errors;
            response.Warnings = warnings;
            return response;
        }

        response.UncDirectory = createResult.Data.UncDirectory;
        var testFile = Path.Combine(createResult.Data.PhysicalPath, $".excpanel-write-test-{Guid.NewGuid():N}.tmp");

        try
        {
            await File.WriteAllTextAsync(testFile, "excpanel-setup-write-test", cancellationToken);
            response.LocalWriteSucceeded = true;
            File.Delete(testFile);
        }
        catch (Exception ex)
        {
            errors.Add($"Local write test failed: {ex.Message}");
        }

        response.Success = errors.Count == 0;
        response.TestFilePath = testFile;
        response.Warnings = warnings;
        response.Errors = errors;
        return response;
    }

    private async Task<bool> ResolveStorageMountedAsync(string mountPath, CancellationToken cancellationToken)
    {
        if (await _mountChecker.IsMountedAsync(mountPath, cancellationToken))
        {
            return true;
        }

        return await _mountRecovery.EnsureMountedAsync(mountPath, cancellationToken);
    }

    private async Task PersistSetupConfigAsync(SetupPlanRequest request, CancellationToken cancellationToken)
    {
        var document = await _setupConfigStore.GetAsync(cancellationToken);
        if (request.Storage is not null)
        {
            document.Storage = request.Storage;
        }

        if (request.Domain is not null)
        {
            document.Domain = request.Domain;
        }

        if (request.Samba is not null)
        {
            document.Samba = request.Samba;
        }

        if (request.Sftp is not null)
        {
            document.Sftp = request.Sftp;
        }

        await _setupConfigStore.SaveAsync(document, cancellationToken);
    }

    private static void AddPlanStep(
        SetupPlanResponse response,
        string stepName,
        string description,
        bool willRun,
        bool willSkip,
        string? skipReason)
    {
        response.Steps.Add(new SetupPlanStep
        {
            StepName = stepName,
            Description = description,
            WillRun = willRun,
            WillSkip = willSkip,
            SkipReason = skipReason
        });
    }

    private async Task RunStepAsync(SetupState state, string stepName, Func<Task> action, CancellationToken cancellationToken)
    {
        var step = GetOrCreateStep(state, stepName);
        step.Status = SetupStepStatus.Running;
        step.StartedAtUtc = DateTime.UtcNow;
        state.CurrentStep = stepName;
        await _stateStore.SaveAsync(state, cancellationToken);

        try
        {
            await action();
            step.Status = SetupStepStatus.Completed;
            step.CompletedAtUtc = DateTime.UtcNow;
            step.Message = $"{stepName} completed.";
        }
        catch (Exception ex)
        {
            step.Status = SetupStepStatus.Failed;
            step.CompletedAtUtc = DateTime.UtcNow;
            step.Message = ex.Message;
            step.ValidationErrors = [ex.Message];
            await _stateStore.SaveAsync(state, cancellationToken);
            throw;
        }

        await _stateStore.SaveAsync(state, cancellationToken);
    }

    private async Task SkipStepAsync(SetupState state, string stepName, string reason, CancellationToken cancellationToken)
    {
        var step = GetOrCreateStep(state, stepName);
        step.Status = SetupStepStatus.Skipped;
        step.Message = reason;
        step.CompletedAtUtc = DateTime.UtcNow;
        state.CurrentStep = stepName;
        await _stateStore.SaveAsync(state, cancellationToken);
    }

    private static SetupStepState GetOrCreateStep(SetupState state, string stepName)
    {
        var step = state.Steps.FirstOrDefault(s => s.StepName == stepName);
        if (step is null)
        {
            step = new SetupStepState { StepName = stepName };
            state.Steps.Add(step);
        }

        return step;
    }

    private static SetupStatusResponse MapStatus(SetupState state) => new()
    {
        Status = state.Status,
        CurrentStep = state.CurrentStep,
        Steps = state.Steps,
        LastError = state.LastError,
        StartedAtUtc = state.StartedAtUtc,
        CompletedAtUtc = state.CompletedAtUtc
    };
}
