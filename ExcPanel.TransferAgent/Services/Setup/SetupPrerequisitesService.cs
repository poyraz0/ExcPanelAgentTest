using System.Runtime.InteropServices;
using ExcPanel.TransferAgent.Contracts;
using ExcPanel.TransferAgent.Models.Setup;
using ExcPanel.TransferAgent.Options;
using ExcPanel.TransferAgent.Services.Samba;
using ExcPanel.TransferAgent.Services.Sftp;
using Microsoft.Extensions.Options;

namespace ExcPanel.TransferAgent.Services.Setup;

public class SetupPrerequisitesService : ISetupPrerequisitesService
{
    private static readonly string[] RequiredPackages =
    [
        "parted",
        "e2fsprogs",
        "samba",
        "winbind",
        "krb5-user",
        "smbclient",
        "acl",
        "openssh-server"
    ];

    private readonly IPlatformInfoService _platformInfoService;
    private readonly ISafeProcessRunner _processRunner;
    private readonly TransferAgentOptions _agentOptions;
    private readonly SetupOptions _setupOptions;
    private readonly SambaOptions _sambaOptions;
    private readonly SambaProbeService _sambaProbeService;
    private readonly IStorageMountChecker _mountChecker;
    private readonly IStorageService _storageService;
    private readonly ISambaProvider _sambaProvider;
    private readonly ISftpStateStore _sftpStateStore;
    private readonly ISftpService _sftpService;

    public SetupPrerequisitesService(
        IPlatformInfoService platformInfoService,
        ISafeProcessRunner processRunner,
        IOptions<TransferAgentOptions> agentOptions,
        IOptions<SetupOptions> setupOptions,
        IOptions<SambaOptions> sambaOptions,
        SambaProbeService sambaProbeService,
        IStorageMountChecker mountChecker,
        IStorageService storageService,
        ISambaProvider sambaProvider,
        ISftpStateStore sftpStateStore,
        ISftpService sftpService)
    {
        _platformInfoService = platformInfoService;
        _processRunner = processRunner;
        _agentOptions = agentOptions.Value;
        _setupOptions = setupOptions.Value;
        _sambaOptions = sambaOptions.Value;
        _sambaProbeService = sambaProbeService;
        _mountChecker = mountChecker;
        _storageService = storageService;
        _sambaProvider = sambaProvider;
        _sftpStateStore = sftpStateStore;
        _sftpService = sftpService;
    }

    public async Task<SetupPrerequisitesResponse> CheckAsync(CancellationToken cancellationToken = default)
    {
        var checks = new List<SetupPrerequisiteItem>();

        var isLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
        checks.Add(new SetupPrerequisiteItem
        {
            Name = "platform-linux",
            Status = isLinux ? SetupPrerequisiteStatus.Passed : SetupPrerequisiteStatus.Failed,
            Message = isLinux ? "Platform is Linux." : "Transfer agent setup requires Linux.",
            Remediation = isLinux ? null : "Deploy the agent on a supported Linux host."
        });

        checks.Add(new SetupPrerequisiteItem
        {
            Name = "agent-service",
            Status = SetupPrerequisiteStatus.Passed,
            Message = "Agent service is running."
        });

        var helperInstalled = File.Exists(_agentOptions.PrivilegedHelperPath);
        checks.Add(new SetupPrerequisiteItem
        {
            Name = "privileged-helper-installed",
            Status = helperInstalled ? SetupPrerequisiteStatus.Passed : SetupPrerequisiteStatus.Failed,
            Message = helperInstalled
                ? "Privileged helper is installed."
                : "Privileged helper is not installed.",
            Remediation = helperInstalled
                ? null
                : $"Install the helper at {_agentOptions.PrivilegedHelperPath}."
        });

        var sudoersOk = await CheckSudoersHelperAsync(cancellationToken);
        checks.Add(new SetupPrerequisiteItem
        {
            Name = "sudoers-helper",
            Status = sudoersOk ? SetupPrerequisiteStatus.Passed : SetupPrerequisiteStatus.Warning,
            Message = sudoersOk
                ? "Privileged helper sudo authorization appears available."
                : "Could not verify sudo authorization for privileged helper.",
            Remediation = sudoersOk ? null : "Configure sudoers to allow the agent user to run the privileged helper."
        });

        foreach (var package in RequiredPackages)
        {
            var installed = isLinux && await IsPackageInstalledAsync(package, cancellationToken);
            checks.Add(new SetupPrerequisiteItem
            {
                Name = $"package-{package}",
                Status = installed ? SetupPrerequisiteStatus.Passed : SetupPrerequisiteStatus.Failed,
                Message = installed ? $"Package '{package}' is installed." : $"Package '{package}' is not installed.",
                Remediation = installed ? null : $"Install package: sudo apt-get install -y {package}"
            });
        }

        if (isLinux)
        {
            var sshActive = await _sambaProbeService.IsServiceActiveAsync("ssh", cancellationToken)
                || await _sambaProbeService.IsServiceActiveAsync("sshd", cancellationToken);
            checks.Add(new SetupPrerequisiteItem
            {
                Name = "ssh-service",
                Status = sshActive ? SetupPrerequisiteStatus.Passed : SetupPrerequisiteStatus.Failed,
                Message = sshActive ? "SSH service is running." : "SSH service is not running.",
                Remediation = sshActive ? null : "Start SSH: sudo systemctl enable --now ssh"
            });

            var smbdInstalled = await _sambaProbeService.IsPackageInstalledAsync("samba", cancellationToken);
            var winbindInstalled = await _sambaProbeService.IsPackageInstalledAsync("winbind", cancellationToken);
            checks.Add(new SetupPrerequisiteItem
            {
                Name = "smbd-installed",
                Status = smbdInstalled ? SetupPrerequisiteStatus.Passed : SetupPrerequisiteStatus.Failed,
                Message = smbdInstalled ? "smbd (samba) is installed." : "smbd (samba) is not installed."
            });
            checks.Add(new SetupPrerequisiteItem
            {
                Name = "winbind-installed",
                Status = winbindInstalled ? SetupPrerequisiteStatus.Passed : SetupPrerequisiteStatus.Failed,
                Message = winbindInstalled ? "winbind is installed." : "winbind is not installed."
            });
        }

        var storageRoot = _agentOptions.StorageRootPath;
        var storageExists = Directory.Exists(storageRoot);
        checks.Add(new SetupPrerequisiteItem
        {
            Name = "storage-root-exists",
            Status = storageExists ? SetupPrerequisiteStatus.Passed : SetupPrerequisiteStatus.Warning,
            Message = storageExists
                ? $"Storage root exists at {storageRoot}."
                : $"Storage root does not exist at {storageRoot}.",
            Remediation = storageExists ? null : "Configure storage or create the storage root path."
        });

        var storageMounted = storageExists && await _mountChecker.IsMountedAsync(storageRoot, cancellationToken);
        checks.Add(new SetupPrerequisiteItem
        {
            Name = "storage-root-mounted",
            Status = storageMounted ? SetupPrerequisiteStatus.Passed : SetupPrerequisiteStatus.Warning,
            Message = storageMounted
                ? $"Storage root is mounted at {storageRoot}."
                : $"Storage root is not mounted at {storageRoot}.",
            Remediation = storageMounted ? null : "Configure and mount transfer storage."
        });

        var dnsOk = await CheckDnsAsync(cancellationToken);
        checks.Add(new SetupPrerequisiteItem
        {
            Name = "dns-resolution",
            Status = dnsOk ? SetupPrerequisiteStatus.Passed : SetupPrerequisiteStatus.Warning,
            Message = dnsOk ? "DNS resolution is working." : "DNS resolution check failed.",
            Remediation = dnsOk ? null : "Verify /etc/resolv.conf and DNS reachability."
        });

        var dcReachable = await CheckDcReachabilityAsync(cancellationToken);
        checks.Add(new SetupPrerequisiteItem
        {
            Name = "dc-reachability",
            Status = dcReachable ? SetupPrerequisiteStatus.Passed : SetupPrerequisiteStatus.Warning,
            Message = dcReachable ? "Domain controller appears reachable." : "Domain controller reachability could not be verified.",
            Remediation = dcReachable ? null : "Verify network connectivity to the domain controller."
        });

        var (domainJoined, joinedDomain, _) = await _sambaProbeService.GetDomainJoinStatusAsync(cancellationToken);
        checks.Add(new SetupPrerequisiteItem
        {
            Name = "domain-join-status",
            Status = domainJoined ? SetupPrerequisiteStatus.Passed : SetupPrerequisiteStatus.Warning,
            Message = domainJoined
                ? $"Joined to domain '{joinedDomain}'."
                : "Server is not joined to Active Directory.",
            Remediation = domainJoined ? null : "Join the server to Active Directory during setup."
        });

        var groupResolved = await _sambaProbeService.IsAdGroupResolvedAsync(_sambaOptions.RequiredAdGroup, cancellationToken);
        checks.Add(new SetupPrerequisiteItem
        {
            Name = "exchange-trusted-subsystem-group",
            Status = groupResolved ? SetupPrerequisiteStatus.Passed : SetupPrerequisiteStatus.Warning,
            Message = groupResolved
                ? $"AD group '{_sambaOptions.RequiredAdGroup}' is resolvable."
                : $"AD group '{_sambaOptions.RequiredAdGroup}' could not be resolved.",
            Remediation = groupResolved ? null : "Join domain and ensure winbind is running."
        });

        var sftpState = await _sftpStateStore.GetAsync(cancellationToken);
        checks.Add(new SetupPrerequisiteItem
        {
            Name = "sftp-initialized",
            Status = sftpState.Initialized ? SetupPrerequisiteStatus.Passed : SetupPrerequisiteStatus.Warning,
            Message = sftpState.Initialized ? "SFTP is initialized." : "SFTP is not initialized.",
            Remediation = sftpState.Initialized ? null : "Initialize SFTP during setup."
        });

        var sambaStatus = await _sambaProvider.GetStatusAsync(cancellationToken);
        checks.Add(new SetupPrerequisiteItem
        {
            Name = "samba-share-configured",
            Status = sambaStatus.ShareConfigured ? SetupPrerequisiteStatus.Passed : SetupPrerequisiteStatus.Warning,
            Message = sambaStatus.ShareConfigured
                ? $"Samba share '{sambaStatus.ShareName}' is configured."
                : "Samba share is not configured.",
            Remediation = sambaStatus.ShareConfigured ? null : "Initialize Samba during setup."
        });

        var ready = checks.All(c => c.Status is SetupPrerequisiteStatus.Passed or SetupPrerequisiteStatus.Warning or SetupPrerequisiteStatus.Skipped);
        return new SetupPrerequisitesResponse
        {
            Ready = ready && checks.All(c => c.Status != SetupPrerequisiteStatus.Failed),
            Checks = checks
        };
    }

    private async Task<bool> IsPackageInstalledAsync(string packageName, CancellationToken cancellationToken)
    {
        var result = await _processRunner.RunAsync(
            "dpkg-query",
            ["-W", "-f=${Status}", packageName],
            cancellationToken: cancellationToken);

        return result.ExitCode == 0 &&
               result.Stdout.Contains("install ok installed", StringComparison.Ordinal);
    }

    private async Task<bool> CheckSudoersHelperAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_agentOptions.PrivilegedHelperPath))
        {
            return false;
        }

        var result = await _processRunner.RunAsync(
            "sudo",
            ["-n", "-l", _agentOptions.PrivilegedHelperPath],
            cancellationToken: cancellationToken);

        return result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.Stdout);
    }

    private async Task<bool> CheckDnsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var host = _platformInfoService.GetPlatformInfo().MachineName;
            var entry = await System.Net.Dns.GetHostEntryAsync(host, cancellationToken);
            return !string.IsNullOrWhiteSpace(entry.HostName);
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> CheckDcReachabilityAsync(CancellationToken cancellationToken)
    {
        var ping = await _processRunner.RunAsync(
            "wbinfo",
            ["--ping-dc"],
            cancellationToken: cancellationToken);

        if (ping.ExitCode == 0)
        {
            return true;
        }

        return false;
    }
}
