using System.Text.Json;
using ExcPanel.TransferAgent.Contracts;
using ExcPanel.TransferAgent.Models;
using ExcPanel.TransferAgent.Options;
using ExcPanel.TransferAgent.Providers.Linux;
using ExcPanel.TransferAgent.Providers.Windows;
using ExcPanel.TransferAgent.Services.Samba;
using ExcPanel.TransferAgent.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ExcPanel.TransferAgent.Tests.Samba;

public class LinuxSambaProviderTests
{
    [Fact]
    public void BuildUncPath_RejectsPathOutsideStorageRoot()
    {
        var provider = CreateProvider(new FakeSafeProcessRunner(), storageRoot: "/data/excpanel-transfer");
        var jobId = Guid.Parse("7f3a2b1c-0000-0000-0000-000000000001");

        var result = provider.BuildUncPath(jobId, JobDirectoryType.Export);

        Assert.Equal(SambaOperationStatus.Success, result.Status);
        Assert.StartsWith("/data/excpanel-transfer", result.Data!.PhysicalDirectory);
    }

    [Fact]
    public void BuildUncPath_RejectsInvalidShareName()
    {
        var provider = CreateProvider(
            new FakeSafeProcessRunner(),
            shareName: "../evil");
        var result = provider.BuildUncPath(Guid.NewGuid(), JobDirectoryType.Export);

        Assert.Equal(SambaOperationStatus.ValidationFailed, result.Status);
    }

    [Fact]
    public async Task InitializeAsync_BlocksWhenDomainNotJoined()
    {
        var runner = new FakeSafeProcessRunner();
        runner.SetHandler("getent", _ => new CommandResult { ExitCode = 1 });
        runner.SetHandler("wbinfo", _ => new CommandResult { ExitCode = 1 });
        runner.SetHandler("dpkg-query", _ => new CommandResult { ExitCode = 0, Stdout = "install ok installed" });
        runner.SetHandler("systemctl", _ => new CommandResult { ExitCode = 0 });

        var provider = CreateProvider(
            runner,
            new FakePrivilegedCommandExecutor
            {
                Result = new TestDomainJoinResult
                {
                    DomainJoined = false,
                    Stderr = "Join to domain is not valid"
                }
            },
            requireDomainJoined: true);

        var dryRun = await provider.BuildConfigurationPlanAsync();

        Assert.False(dryRun.Valid);
        Assert.Contains(dryRun.ValidationErrors, error => error.Contains("Active Directory", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(dryRun.ValidationErrors, error => error.Contains("Join to domain is not valid", StringComparison.OrdinalIgnoreCase));

        var initialize = await provider.InitializeAsync();
        Assert.Equal(SambaOperationStatus.ValidationFailed, initialize.Status);
    }

    [Fact]
    public async Task InitializeAsync_BlocksWhenRequiredAdGroupMissing()
    {
        var runner = new FakeSafeProcessRunner();
        runner.SetHandler("dpkg-query", _ => new CommandResult { ExitCode = 0, Stdout = "install ok installed" });
        runner.SetHandler("systemctl", _ => new CommandResult { ExitCode = 0 });
        runner.SetHandler("getent", _ => new CommandResult { ExitCode = 1 });
        runner.SetHandler("wbinfo", _ => new CommandResult { ExitCode = 1 });

        var provider = CreateProvider(runner, CreateJoinedDomainExecutor());
        var result = await provider.InitializeAsync();

        Assert.Equal(SambaOperationStatus.ValidationFailed, result.Status);
        Assert.Contains("could not be resolved", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WindowsProvider_ReturnsNotSupported()
    {
        var provider = new WindowsSmbProvider();
        var result = provider.BuildUncPath(Guid.NewGuid(), JobDirectoryType.Export);

        Assert.Equal(SambaOperationStatus.NotSupported, result.Status);
    }

    private static FakePrivilegedCommandExecutor CreateJoinedDomainExecutor() =>
        new()
        {
            Result = new TestDomainJoinResult { DomainJoined = true }
        };

    private static LinuxSambaProvider CreateProvider(
        FakeSafeProcessRunner runner,
        IPrivilegedCommandExecutor? privilegedCommandExecutor = null,
        string storageRoot = "/data/excpanel-transfer",
        string shareName = "PSTTransfer$",
        bool requireDomainJoined = true)
    {
        var sambaOptions = Microsoft.Extensions.Options.Options.Create(new SambaOptions
        {
            Enabled = true,
            ShareName = shareName,
            ServerName = "TRANSFER01",
            StorageRoot = storageRoot,
            RequiredAdGroup = @"DOGRU\Exchange Trusted Subsystem",
            RequireDomainJoined = requireDomainJoined
        });
        var agentOptions = Microsoft.Extensions.Options.Options.Create(new TransferAgentOptions
        {
            StorageRootPath = storageRoot,
            RequireStorageMount = false
        });

        return new LinuxSambaProvider(
            sambaOptions,
            agentOptions,
            new SambaPathService(sambaOptions, agentOptions),
            new SambaProbeService(runner, privilegedCommandExecutor ?? CreateJoinedDomainExecutor(), sambaOptions),
            new FakeStorageMountChecker { IsMounted = true },
            new LinuxJobDirectoryProvider(
                agentOptions,
                sambaOptions,
                new FakeStorageMountChecker { IsMounted = true },
                new SambaPathService(sambaOptions, agentOptions),
                new FakeExchangeAclService(),
                NullLogger<LinuxJobDirectoryProvider>.Instance),
            new FakePrivilegedHelperClient(),
            NullLogger<LinuxSambaProvider>.Instance);
    }
}

internal sealed class FakeSafeProcessRunner : ISafeProcessRunner
{
    private readonly Dictionary<string, Func<IReadOnlyList<string>, CommandResult>> _handlers =
        new(StringComparer.Ordinal);

    public void SetHandler(string command, Func<IReadOnlyList<string>, CommandResult> handler) =>
        _handlers[command] = handler;

    public Task<CommandResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        int timeoutSeconds = 120,
        CancellationToken cancellationToken = default)
    {
        var key = Path.GetFileName(fileName);
        if (_handlers.TryGetValue(key, out var handler))
        {
            return Task.FromResult(handler(arguments));
        }

        return Task.FromResult(new CommandResult
        {
            Command = fileName,
            Args = string.Join(' ', arguments),
            ExitCode = 0
        });
    }
}
