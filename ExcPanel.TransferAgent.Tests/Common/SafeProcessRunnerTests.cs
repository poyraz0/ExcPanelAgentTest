using ExcPanel.TransferAgent.Common;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ExcPanel.TransferAgent.Tests.Common;

public class SafeProcessRunnerTests
{
    [Fact]
    public async Task RunAsync_ReturnsFailureResult_WhenCommandDoesNotExist()
    {
        var runner = new SafeProcessRunner(NullLogger<SafeProcessRunner>.Instance);

        var result = await runner.RunAsync(
            "excpanel-nonexistent-command-xyz",
            ["--test"],
            timeoutSeconds: 5);

        Assert.Equal(-1, result.ExitCode);
        Assert.False(string.IsNullOrWhiteSpace(result.Stderr));
    }
}
