using ExcPanel.TransferAgent.Contracts;
using ExcPanel.TransferAgent.Options;
using ExcPanel.TransferAgent.PrivilegedHelper.Handlers;
using ExcPanel.TransferAgent.Tests.Fakes;

namespace ExcPanel.TransferAgent.Tests.Samba;

public class TestDomainJoinHandlerTests
{
    [Fact]
    public async Task HandleAsync_ReturnsJoinedTrue_WhenExitCodeIsZero()
    {
        var runner = new FakePrivilegedCommandRunner();
        runner.SetHandler("net", args =>
        {
            Assert.Equal(["ads", "testjoin"], args);
            return new CommandExecutionResult { ExitCode = 0, Stdout = "Join is OK" };
        });

        var handler = new TestDomainJoinHandler(runner);
        var response = await handler.HandleAsync("req-1", CancellationToken.None);

        Assert.True(response.Success);
        Assert.True(response.Data.GetProperty("domainJoined").GetBoolean());
    }

    [Fact]
    public async Task HandleAsync_ReturnsJoinedFalse_WithStderr_WhenExitCodeIsNonZero()
    {
        var runner = new FakePrivilegedCommandRunner();
        runner.SetHandler("net", _ => new CommandExecutionResult
        {
            ExitCode = 1,
            Stderr = "Failed to join domain"
        });

        var handler = new TestDomainJoinHandler(runner);
        var response = await handler.HandleAsync("req-2", CancellationToken.None);

        Assert.True(response.Success);
        Assert.False(response.Data.GetProperty("domainJoined").GetBoolean());
        Assert.Equal("Failed to join domain", response.Data.GetProperty("stderr").GetString());
    }
}
