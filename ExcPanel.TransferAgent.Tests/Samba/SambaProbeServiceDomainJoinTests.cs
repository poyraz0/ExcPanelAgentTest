using System.Text.Json;
using ExcPanel.TransferAgent.Contracts;
using ExcPanel.TransferAgent.Options;
using ExcPanel.TransferAgent.Services.PrivilegedHelper;
using ExcPanel.TransferAgent.Services.Samba;
using ExcPanel.TransferAgent.Tests.Fakes;
using Microsoft.Extensions.Options;

namespace ExcPanel.TransferAgent.Tests.Samba;

public class SambaProbeServiceDomainJoinTests
{
    [Fact]
    public async Task GetDomainJoinStatusAsync_UsesTestJoinResult_EvenWhenRealmListFails()
    {
        var runner = new FakeSafeProcessRunner();
        runner.SetHandler("realm", _ => new Models.CommandResult { ExitCode = 1 });

        var executor = new FakePrivilegedCommandExecutor
        {
            Result = new TestDomainJoinResult { DomainJoined = true }
        };

        var service = CreateProbeService(runner, executor);
        var (joined, domain, joinError) = await service.GetDomainJoinStatusAsync(CancellationToken.None);

        Assert.True(joined);
        Assert.Null(domain);
        Assert.Null(joinError);
    }

    [Fact]
    public async Task GetDomainJoinStatusAsync_ReturnsJoinedDomainFromRealm_WhenAvailable()
    {
        var runner = new FakeSafeProcessRunner();
        runner.SetHandler("realm", _ => new Models.CommandResult { ExitCode = 0, Stdout = "corp.local\n" });

        var executor = new FakePrivilegedCommandExecutor
        {
            Result = new TestDomainJoinResult { DomainJoined = true }
        };

        var service = CreateProbeService(runner, executor);
        var (joined, domain, _) = await service.GetDomainJoinStatusAsync(CancellationToken.None);

        Assert.True(joined);
        Assert.Equal("corp.local", domain);
    }

    [Fact]
    public async Task GetDomainJoinStatusAsync_ReturnsStderr_WhenTestJoinFails()
    {
        var runner = new FakeSafeProcessRunner();
        var executor = new FakePrivilegedCommandExecutor
        {
            Result = new TestDomainJoinResult
            {
                DomainJoined = false,
                Stderr = "NT_STATUS_NO_TRUST_SAM_ACCOUNT"
            }
        };

        var service = CreateProbeService(runner, executor);
        var (joined, _, joinError) = await service.GetDomainJoinStatusAsync(CancellationToken.None);

        Assert.False(joined);
        Assert.Equal("NT_STATUS_NO_TRUST_SAM_ACCOUNT", joinError);
    }

    [Fact]
    public async Task LinuxPrivilegedCommandExecutor_MapsHelperSuccessPayload()
    {
        var helper = new FakePrivilegedHelperClient
        {
            Handler = request =>
            {
                Assert.Equal(PrivilegedHelperActions.TestDomainJoin, request.Action);
                var data = JsonSerializer.SerializeToElement(new TestDomainJoinResultData
                {
                    DomainJoined = true
                }, PrivilegedHelperJson.SerializerOptions);

                return PrivilegedHelperResponse.Ok(
                    request.RequestId,
                    "Domain join verification succeeded.",
                    ["TestDomainJoin"],
                    data);
            }
        };

        var executor = new LinuxPrivilegedCommandExecutor(helper);
        var result = await executor.TestDomainJoinAsync();

        Assert.True(result.DomainJoined);
    }

    private static SambaProbeService CreateProbeService(
        FakeSafeProcessRunner runner,
        IPrivilegedCommandExecutor executor)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new SambaOptions
        {
            RequireDomainJoined = true,
            RequiredAdGroup = @"DOGRU\Exchange Trusted Subsystem"
        });

        return new SambaProbeService(runner, executor, options);
    }
}

internal sealed class FakePrivilegedCommandExecutor : IPrivilegedCommandExecutor
{
    public TestDomainJoinResult Result { get; set; } = new() { DomainJoined = false };

    public Task<TestDomainJoinResult> TestDomainJoinAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Result);
}
