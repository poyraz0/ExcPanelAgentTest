using System.Runtime.InteropServices;
using System.Text.Json;
using ExcPanel.TransferAgent.Contracts;
using ExcPanel.TransferAgent.Models.Domain;
using ExcPanel.TransferAgent.Options;
using ExcPanel.TransferAgent.Services.Samba;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExcPanel.TransferAgent.Services.Domain;

public class DomainService : IDomainService
{
    private readonly SambaOptions _sambaOptions;
    private readonly TransferAgentOptions _agentOptions;
    private readonly SambaProbeService _probeService;
    private readonly IPrivilegedHelperClient _privilegedHelperClient;
    private readonly IPrivilegedCommandExecutor _privilegedCommandExecutor;
    private readonly ISafeProcessRunner _processRunner;
    private readonly ILogger<DomainService> _logger;

    public DomainService(
        IOptions<SambaOptions> sambaOptions,
        IOptions<TransferAgentOptions> agentOptions,
        SambaProbeService probeService,
        IPrivilegedHelperClient privilegedHelperClient,
        IPrivilegedCommandExecutor privilegedCommandExecutor,
        ISafeProcessRunner processRunner,
        ILogger<DomainService> logger)
    {
        _sambaOptions = sambaOptions.Value;
        _agentOptions = agentOptions.Value;
        _probeService = probeService;
        _privilegedHelperClient = privilegedHelperClient;
        _privilegedCommandExecutor = privilegedCommandExecutor;
        _processRunner = processRunner;
        _logger = logger;
    }

    public async Task<DomainStatusResponse> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var (joined, domain, _) = await _probeService.GetDomainJoinStatusAsync(cancellationToken);
        var groupResolved = await _probeService.IsAdGroupResolvedAsync(_sambaOptions.RequiredAdGroup, cancellationToken);
        var smbdRunning = await _probeService.IsServiceActiveAsync("smbd", cancellationToken);
        var winbindRunning = await _probeService.IsServiceActiveAsync("winbind", cancellationToken);
        var dnsOk = await CheckDnsAsync(cancellationToken);
        var dcReachable = await CheckDcPingAsync(cancellationToken);

        return new DomainStatusResponse
        {
            Supported = RuntimeInformation.IsOSPlatform(OSPlatform.Linux),
            DomainJoined = joined,
            JoinedDomain = domain,
            JoinedRealm = domain?.ToUpperInvariant(),
            DnsResolvable = dnsOk,
            DcReachable = dcReachable,
            SmbdRunning = smbdRunning,
            WinbindRunning = winbindRunning,
            RequiredAdGroupResolved = groupResolved,
            RequiredAdGroup = _sambaOptions.RequiredAdGroup
        };
    }

    public async Task<DomainPrecheckResponse> PrecheckAsync(DomainPrecheckRequest request, CancellationToken cancellationToken = default)
    {
        var checks = new List<DomainPrecheckItem>();
        var warnings = new List<string>();

        var (joined, joinedDomain, _) = await _probeService.GetDomainJoinStatusAsync(cancellationToken);
        if (joined)
        {
            return new DomainPrecheckResponse
            {
                Ready = true,
                AlreadyJoined = true,
                Checks =
                [
                    new DomainPrecheckItem
                    {
                        Name = "domain-join",
                        Passed = true,
                        Message = $"Already joined to domain '{joinedDomain}'."
                    }
                ]
            };
        }

        checks.Add(new DomainPrecheckItem
        {
            Name = "dns-domain",
            Passed = !string.IsNullOrWhiteSpace(request.DnsDomain),
            Message = string.IsNullOrWhiteSpace(request.DnsDomain) ? "dnsDomain is required." : null
        });
        checks.Add(new DomainPrecheckItem
        {
            Name = "realm",
            Passed = !string.IsNullOrWhiteSpace(request.Realm),
            Message = string.IsNullOrWhiteSpace(request.Realm) ? "realm is required." : null
        });
        checks.Add(new DomainPrecheckItem
        {
            Name = "domain-controller",
            Passed = !string.IsNullOrWhiteSpace(request.DomainController),
            Message = string.IsNullOrWhiteSpace(request.DomainController) ? "domainController is required." : null
        });

        var dnsResolved = await ResolveHostAsync(request.DomainController, cancellationToken);
        checks.Add(new DomainPrecheckItem
        {
            Name = "dns-resolution",
            Passed = dnsResolved,
            Message = dnsResolved ? null : $"Could not resolve domain controller '{request.DomainController}'.",
            Remediation = dnsResolved ? null : "Verify DNS configuration."
        });

        var dcPing = await PingHostAsync(request.DomainControllerIp, cancellationToken);
        checks.Add(new DomainPrecheckItem
        {
            Name = "dc-reachability",
            Passed = dcPing,
            Message = dcPing ? null : $"Domain controller IP '{request.DomainControllerIp}' is not reachable.",
            Remediation = dcPing ? null : "Verify network connectivity to the domain controller."
        });

        var krbInstalled = await _probeService.IsPackageInstalledAsync("krb5-user", cancellationToken);
        checks.Add(new DomainPrecheckItem
        {
            Name = "krb5-user",
            Passed = krbInstalled,
            Message = krbInstalled ? null : "krb5-user package is not installed.",
            Remediation = krbInstalled ? null : "sudo apt-get install -y krb5-user"
        });

        return new DomainPrecheckResponse
        {
            Ready = checks.All(c => c.Passed),
            AlreadyJoined = false,
            Checks = checks,
            Warnings = warnings
        };
    }

    public async Task<DomainJoinResponse> JoinAsync(DomainJoinRequest request, CancellationToken cancellationToken = default)
    {
        var (joined, joinedDomain, _) = await _probeService.GetDomainJoinStatusAsync(cancellationToken);
        if (joined)
        {
            return new DomainJoinResponse
            {
                Joined = true,
                Skipped = true,
                JoinedDomain = joinedDomain,
                CompletedSteps = ["DomainJoinSkipped"]
            };
        }

        if (string.IsNullOrWhiteSpace(request.Password))
        {
            throw new InvalidOperationException("Domain join password is required.");
        }

        // Password is passed only to helper via JSON stdin; never logged.
        var helperRequest = new PrivilegedHelperRequest
        {
            RequestId = Guid.NewGuid().ToString("D"),
            Action = PrivilegedHelperActions.DomainJoin,
            Payload = JsonSerializer.SerializeToElement(new DomainJoinPayload
            {
                DnsDomain = request.DnsDomain,
                Realm = request.Realm,
                Workgroup = request.Workgroup,
                DomainController = request.DomainController,
                DomainControllerIp = request.DomainControllerIp,
                Username = request.Username,
                Password = request.Password,
                ComputerName = request.ComputerName,
                ComputerOu = request.ComputerOu,
                RequiredAdGroup = request.RequiredAdGroup,
                MainConfigPath = _sambaOptions.MainConfigPath
            }, PrivilegedHelperJson.SerializerOptions)
        };

        _logger.LogInformation(
            "Starting domain join for user {Username} to realm {Realm}",
            request.Username,
            request.Realm);

        var response = await _privilegedHelperClient.InvokeAsync(helperRequest, cancellationToken);
        if (!response.Success)
        {
            throw new InvalidOperationException(response.Message ?? "Domain join failed.");
        }

        var result = response.Data.Deserialize<DomainJoinResultData>(PrivilegedHelperJson.SerializerOptions);
        return new DomainJoinResponse
        {
            Joined = result?.Joined ?? true,
            Skipped = result?.Skipped ?? false,
            JoinedDomain = result?.JoinedDomain ?? request.DnsDomain,
            CompletedSteps = response.CompletedSteps.ToList()
        };
    }

    public async Task<DomainTestResponse> TestAsync(CancellationToken cancellationToken = default)
    {
        var testJoin = await _privilegedCommandExecutor.TestDomainJoinAsync(cancellationToken);
        var dcPing = await CheckDcPingAsync(cancellationToken);
        var groupResolved = await _probeService.IsAdGroupResolvedAsync(_sambaOptions.RequiredAdGroup, cancellationToken);

        return new DomainTestResponse
        {
            DomainJoined = testJoin.DomainJoined,
            DcPingSucceeded = dcPing,
            TestJoinSucceeded = testJoin.DomainJoined,
            RequiredAdGroupResolved = groupResolved,
            Message = testJoin.DomainJoined ? "Domain join verification succeeded." : testJoin.ErrorMessage ?? "Domain join verification failed."
        };
    }

    private async Task<bool> CheckDnsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await System.Net.Dns.GetHostEntryAsync(Environment.MachineName, cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> CheckDcPingAsync(CancellationToken cancellationToken)
    {
        var result = await _processRunner.RunAsync("wbinfo", ["--ping-dc"], cancellationToken: cancellationToken);
        return result.ExitCode == 0;
    }

    private async Task<bool> ResolveHostAsync(string host, CancellationToken cancellationToken)
    {
        try
        {
            await System.Net.Dns.GetHostEntryAsync(host, cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> PingHostAsync(string ip, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ip))
        {
            return false;
        }

        var result = await _processRunner.RunAsync("ping", ["-c", "1", "-W", "2", ip], cancellationToken: cancellationToken);
        return result.ExitCode == 0;
    }
}
