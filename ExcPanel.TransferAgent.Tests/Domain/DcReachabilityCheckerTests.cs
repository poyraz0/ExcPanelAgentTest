using ExcPanel.TransferAgent.Models;
using ExcPanel.TransferAgent.Services.Domain;
using ExcPanel.TransferAgent.Tests.Samba;
using Xunit;

namespace ExcPanel.TransferAgent.Tests.Domain;

public class DcReachabilityCheckerTests
{
    [Fact]
    public async Task IsReachableAsync_ReturnsTrue_WhenPingSucceeds()
    {
        var runner = new FakeSafeProcessRunner();
        runner.SetHandler("ping", _ => new CommandResult { ExitCode = 0 });

        var reachable = await DcReachabilityChecker.IsReachableAsync("10.0.0.1", runner);

        Assert.True(reachable);
    }

    [Fact]
    public async Task IsReachableAsync_ReturnsFalse_WhenHostMissing()
    {
        var runner = new FakeSafeProcessRunner();

        var reachable = await DcReachabilityChecker.IsReachableAsync("", runner);

        Assert.False(reachable);
    }

    [Fact]
    public async Task IsReachableAsync_ReturnsFalse_WhenPingFailsAndTcpPortsClosed()
    {
        var runner = new FakeSafeProcessRunner();
        runner.SetHandler("ping", _ => new CommandResult { ExitCode = 1 });

        var reachable = await DcReachabilityChecker.IsReachableAsync("192.0.2.1", runner);

        Assert.False(reachable);
    }
}
