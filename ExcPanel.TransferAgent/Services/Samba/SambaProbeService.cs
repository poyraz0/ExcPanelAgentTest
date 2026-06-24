using ExcPanel.TransferAgent.Contracts;
using ExcPanel.TransferAgent.Contracts.Validation;
using ExcPanel.TransferAgent.Models;
using ExcPanel.TransferAgent.Options;
using Microsoft.Extensions.Options;

namespace ExcPanel.TransferAgent.Services.Samba;

public class SambaProbeService
{
    private static readonly string[] RequiredPackages =
    [
        "samba",
        "samba-common-bin",
        "winbind",
        "libnss-winbind",
        "libpam-winbind",
        "acl"
    ];

    private static readonly string[] OptionalPackages =
    [
        "realmd",
        "adcli"
    ];

    private readonly ISafeProcessRunner _processRunner;
    private readonly IPrivilegedCommandExecutor _privilegedCommandExecutor;
    private readonly SambaOptions _options;

    public SambaProbeService(
        ISafeProcessRunner processRunner,
        IPrivilegedCommandExecutor privilegedCommandExecutor,
        IOptions<SambaOptions> options)
    {
        _processRunner = processRunner;
        _privilegedCommandExecutor = privilegedCommandExecutor;
        _options = options.Value;
    }

    public async Task<bool> IsPackageInstalledAsync(string packageName, CancellationToken cancellationToken)
    {
        var result = await _processRunner.RunAsync(
            "dpkg-query",
            ["-W", "-f=${Status}", packageName],
            cancellationToken: cancellationToken);

        return result.ExitCode == 0 &&
               result.Stdout.Contains("install ok installed", StringComparison.Ordinal);
    }

    public async Task<bool> IsServiceActiveAsync(string serviceName, CancellationToken cancellationToken)
    {
        var result = await _processRunner.RunAsync(
            "systemctl",
            ["is-active", "--quiet", serviceName],
            cancellationToken: cancellationToken);

        return result.ExitCode == 0;
    }

    public async Task<(bool Joined, string? Domain, string? JoinError)> GetDomainJoinStatusAsync(
        CancellationToken cancellationToken)
    {
        var testJoin = await _privilegedCommandExecutor.TestDomainJoinAsync(cancellationToken);
        var domain = await ResolveJoinedDomainAsync(cancellationToken);

        var joinError = testJoin.ErrorMessage;
        if (!testJoin.DomainJoined && !string.IsNullOrWhiteSpace(testJoin.Stderr))
        {
            joinError = string.IsNullOrWhiteSpace(joinError)
                ? testJoin.Stderr.Trim()
                : $"{joinError} {testJoin.Stderr.Trim()}";
        }

        return (testJoin.DomainJoined, domain, joinError);
    }

    private async Task<string?> ResolveJoinedDomainAsync(CancellationToken cancellationToken)
    {
        var result = await _processRunner.RunAsync(
            "realm",
            ["list"],
            cancellationToken: cancellationToken);

        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.Stdout))
        {
            return null;
        }

        return result.Stdout
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
    }

    public async Task<bool> IsAdGroupResolvedAsync(string requiredAdGroup, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(requiredAdGroup))
        {
            return false;
        }

        var trimmed = requiredAdGroup.Trim();
        var getent = await _processRunner.RunAsync(
            "getent",
            ["group", trimmed],
            cancellationToken: cancellationToken);

        if (getent.ExitCode == 0 && !string.IsNullOrWhiteSpace(getent.Stdout))
        {
            return true;
        }

        var wbinfo = await _processRunner.RunAsync(
            "wbinfo",
            ["--group-info", trimmed],
            cancellationToken: cancellationToken);

        return wbinfo.ExitCode == 0 && !string.IsNullOrWhiteSpace(wbinfo.Stdout);
    }

    public async Task<bool> IsShareListedInTestparmAsync(string shareName, CancellationToken cancellationToken)
    {
        var result = await _processRunner.RunAsync(
            "testparm",
            ["-s"],
            timeoutSeconds: 15,
            cancellationToken: cancellationToken);

        if (result.ExitCode != 0)
        {
            return false;
        }

        return SambaTestparmHelpers.ContainsShareSection(result.Stdout, shareName);
    }

    public async Task<IReadOnlyList<SambaPrerequisiteItem>> BuildPrerequisiteItemsAsync(
        bool storageMounted,
        CancellationToken cancellationToken)
    {
        var items = new List<SambaPrerequisiteItem>();

        foreach (var package in RequiredPackages)
        {
            var installed = await IsPackageInstalledAsync(package, cancellationToken);
            items.Add(new SambaPrerequisiteItem
            {
                Name = package,
                Satisfied = installed,
                Message = installed ? null : $"Package '{package}' is not installed.",
                RecommendedActions = installed
                    ? Array.Empty<string>()
                    : [$"Install package: sudo apt-get install -y {package}"]
            });
        }

        foreach (var package in OptionalPackages)
        {
            var installed = await IsPackageInstalledAsync(package, cancellationToken);
            items.Add(new SambaPrerequisiteItem
            {
                Name = package,
                Satisfied = installed,
                Message = installed ? null : $"Optional package '{package}' is not installed.",
                RecommendedActions = installed
                    ? Array.Empty<string>()
                    : [$"Install package if domain join is required: sudo apt-get install -y {package}"]
            });
        }

        var (joined, domain, _) = await GetDomainJoinStatusAsync(cancellationToken);
        items.Add(new SambaPrerequisiteItem
        {
            Name = "domain-membership",
            Satisfied = !_options.RequireDomainJoined || joined,
            Message = joined
                ? $"Joined to domain '{domain}'."
                : "Server is not joined to an Active Directory domain.",
            RecommendedActions = joined
                ? Array.Empty<string>()
                : ["Join the server to Active Directory using realm join or equivalent."]
        });

        items.Add(new SambaPrerequisiteItem
        {
            Name = "storage-mount",
            Satisfied = storageMounted,
            Message = storageMounted ? null : "Storage root is not mounted.",
            RecommendedActions = storageMounted
                ? Array.Empty<string>()
                : ["Configure and mount transfer storage before initializing Samba."]
        });

        var groupResolved = await IsAdGroupResolvedAsync(_options.RequiredAdGroup, cancellationToken);
        items.Add(new SambaPrerequisiteItem
        {
            Name = "required-ad-group",
            Satisfied = groupResolved,
            Message = groupResolved
                ? $"AD group '{_options.RequiredAdGroup}' is resolvable."
                : $"AD group '{_options.RequiredAdGroup}' could not be resolved.",
            RecommendedActions = groupResolved
                ? Array.Empty<string>()
                : ["Verify winbind is running and the Exchange Trusted Subsystem group is visible on this server."]
        });

        return items;
    }
}
