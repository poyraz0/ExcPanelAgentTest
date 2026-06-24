using ExcPanel.TransferAgent.Contracts;
using ExcPanel.TransferAgent.Options;
using ExcPanel.TransferAgent.Services.Storage;
using Microsoft.Extensions.Options;

namespace ExcPanel.TransferAgent.Tests.Storage;

public class ConfirmationTokenServiceTests
{
    [Fact]
    public void TryConsumeToken_RejectsExpiredToken()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new TransferAgentOptions { ConfirmationTokenLifetimeMinutes = 0 });
        var service = new ConfirmationTokenService(options);
        var identity = new StorageConfirmationIdentity
        {
            DiskPath = "/dev/sdb",
            DiskSerialOrWwn = "SERIAL",
            SizeBytes = 100,
            MountPath = "/data/excpanel-transfer"
        };

        var token = service.IssueToken(identity);
        Thread.Sleep(10);

        var consumed = service.TryConsumeToken(token, identity, out var error);
        Assert.False(consumed);
        Assert.Contains("expired", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryConsumeToken_RejectsMismatchedIdentity()
    {
        var service = new ConfirmationTokenService(Microsoft.Extensions.Options.Options.Create(new TransferAgentOptions()));
        var identity = new StorageConfirmationIdentity
        {
            DiskPath = "/dev/sdb",
            DiskSerialOrWwn = "SERIAL",
            SizeBytes = 100,
            MountPath = "/data/excpanel-transfer"
        };

        var token = service.IssueToken(identity);
        var consumed = service.TryConsumeToken(token, new StorageConfirmationIdentity
        {
            DiskPath = identity.DiskPath,
            DiskSerialOrWwn = identity.DiskSerialOrWwn,
            SizeBytes = 200,
            MountPath = identity.MountPath
        }, out var error);

        Assert.False(consumed);
        Assert.Contains("does not match", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryConsumeToken_RejectsReusedToken()
    {
        var service = new ConfirmationTokenService(Microsoft.Extensions.Options.Options.Create(new TransferAgentOptions()));
        var identity = new StorageConfirmationIdentity
        {
            DiskPath = "/dev/sdb",
            DiskSerialOrWwn = "SERIAL",
            SizeBytes = 100,
            MountPath = "/data/excpanel-transfer"
        };

        var token = service.IssueToken(identity);
        Assert.True(service.TryConsumeToken(token, identity, out _));
        Assert.False(service.TryConsumeToken(token, identity, out var error));
        Assert.Contains("already been used", error, StringComparison.OrdinalIgnoreCase);
    }
}
