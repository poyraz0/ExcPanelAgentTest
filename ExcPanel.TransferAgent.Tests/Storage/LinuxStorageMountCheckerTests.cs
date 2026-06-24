using ExcPanel.TransferAgent.Options;
using ExcPanel.TransferAgent.Providers.Linux;
using Microsoft.Extensions.Options;

namespace ExcPanel.TransferAgent.Tests.Storage;

public class LinuxStorageMountCheckerTests : IDisposable
{
    private readonly string _storageRoot;

    public LinuxStorageMountCheckerTests()
    {
        _storageRoot = Path.Combine(Path.GetTempPath(), $"excpanel-mount-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_storageRoot);
    }

    [Fact]
    public async Task IsMountedAsync_AllowsExistingDirectoryWhenMountNotRequired()
    {
        var checker = new LinuxStorageMountChecker(Microsoft.Extensions.Options.Options.Create(
            new TransferAgentOptions
            {
                RequireStorageMount = false
            }));

        var isMounted = await checker.IsMountedAsync(_storageRoot);

        Assert.True(isMounted);
    }

    [Fact]
    public async Task IsMountedAsync_RejectsMissingDirectoryWhenMountNotRequired()
    {
        var checker = new LinuxStorageMountChecker(Microsoft.Extensions.Options.Options.Create(
            new TransferAgentOptions
            {
                RequireStorageMount = false
            }));

        var isMounted = await checker.IsMountedAsync(
            Path.Combine(Path.GetTempPath(), $"excpanel-missing-{Guid.NewGuid():N}"));

        Assert.False(isMounted);
    }

    public void Dispose()
    {
        if (Directory.Exists(_storageRoot))
        {
            Directory.Delete(_storageRoot, recursive: true);
        }
    }
}
