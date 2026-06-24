using ExcPanel.TransferAgent.Contracts;
using ExcPanel.TransferAgent.Models;
using ExcPanel.TransferAgent.Options;
using ExcPanel.TransferAgent.Providers.Linux;
using ExcPanel.TransferAgent.Services.Samba;
using ExcPanel.TransferAgent.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ExcPanel.TransferAgent.Tests.Storage;

public class JobDirectoryProviderTests : IDisposable
{
    private readonly string _storageRoot;
    private readonly FakeStorageMountChecker _mountChecker = new();
    private readonly LinuxJobDirectoryProvider _provider;

    public JobDirectoryProviderTests()
    {
        _storageRoot = Path.Combine(Path.GetTempPath(), $"excpanel-jobdir-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_storageRoot);
        foreach (var subdirectory in new[] { "exports", "imports", "temp", "logs" })
        {
            Directory.CreateDirectory(Path.Combine(_storageRoot, subdirectory));
        }

        _provider = CreateProvider(_storageRoot, _mountChecker);
    }

    [Fact]
    public async Task CreateAsync_CreatesExportDirectory()
    {
        var jobId = Guid.Parse("7f3a2b1c-0000-0000-0000-000000000001");

        var result = await _provider.CreateAsync(jobId, JobDirectoryType.Export);

        Assert.Equal(JobDirectoryOperationStatus.Success, result.Status);
        Assert.True(result.Data!.Created);
        Assert.Equal("exports/7f3a2b1c-0000-0000-0000-000000000001", result.Data.RelativePath);
        Assert.Equal(
            Path.Combine(_storageRoot, "exports", "7f3a2b1c-0000-0000-0000-000000000001"),
            result.Data.PhysicalPath);
        Assert.True(File.Exists(Path.Combine(result.Data.PhysicalPath, LinuxJobDirectoryProvider.MarkerFileName)));
    }

    [Fact]
    public async Task CreateAsync_CreatesImportDirectory()
    {
        var jobId = Guid.Parse("8f3a2b1c-0000-0000-0000-000000000002");

        var result = await _provider.CreateAsync(jobId, JobDirectoryType.Import);

        Assert.Equal(JobDirectoryOperationStatus.Success, result.Status);
        Assert.True(result.Data!.Created);
        Assert.Equal("imports/8f3a2b1c-0000-0000-0000-000000000002", result.Data.RelativePath);
        Assert.True(Directory.Exists(result.Data.PhysicalPath));
    }

    [Fact]
    public async Task CreateAsync_IsIdempotentForSameJob()
    {
        var jobId = Guid.Parse("9f3a2b1c-0000-0000-0000-000000000003");

        var first = await _provider.CreateAsync(jobId, JobDirectoryType.Export);
        var second = await _provider.CreateAsync(jobId, JobDirectoryType.Export);

        Assert.True(first.Data!.Created);
        Assert.False(second.Data!.Created);
        Assert.Equal(first.Data.PhysicalPath, second.Data.PhysicalPath);
    }

    [Fact]
    public async Task CreateAsync_RejectsMarkerMismatch()
    {
        var jobId = Guid.Parse("af3a2b1c-0000-0000-0000-000000000004");
        var directoryPath = Path.Combine(_storageRoot, "exports", jobId.ToString("D"));
        Directory.CreateDirectory(directoryPath);
        await File.WriteAllTextAsync(
            Path.Combine(directoryPath, LinuxJobDirectoryProvider.MarkerFileName),
            """{"jobId":"00000000-0000-0000-0000-000000000099","jobType":"Export","createdAtUtc":"2026-01-01T00:00:00Z","agentVersion":"1.0.0"}""");

        var result = await _provider.CreateAsync(jobId, JobDirectoryType.Export);

        Assert.Equal(JobDirectoryOperationStatus.Conflict, result.Status);
    }

    [Fact]
    public void TryResolveJobDirectory_KeepsPathsInsideStorageRoot()
    {
        var jobId = Guid.Parse("bf3a2b1c-0000-0000-0000-000000000005");

        var resolved = _provider.TryResolveJobDirectory(
            jobId,
            JobDirectoryType.Export,
            out _,
            out var physicalPath,
            out var error);

        Assert.True(resolved);
        Assert.Null(error);
        Assert.StartsWith(Path.GetFullPath(_storageRoot), physicalPath, StringComparison.Ordinal);
        Assert.False(physicalPath.Contains("..", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CreateAsync_RejectsSymlinkJobDirectory_OnLinux()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var jobId = Guid.Parse("cf3a2b1c-0000-0000-0000-000000000006");
        var targetPath = Path.Combine(_storageRoot, "temp", "symlink-target");
        var symlinkPath = Path.Combine(_storageRoot, "exports", jobId.ToString("D"));
        Directory.CreateDirectory(targetPath);
        Directory.CreateSymbolicLink(symlinkPath, targetPath);

        var result = await _provider.CreateAsync(jobId, JobDirectoryType.Export);

        Assert.Equal(JobDirectoryOperationStatus.Conflict, result.Status);
    }

    [Fact]
    public async Task DeleteAsync_DoesNotDeleteDirectoryWithoutMarker()
    {
        var jobId = Guid.Parse("df3a2b1c-0000-0000-0000-000000000007");
        var directoryPath = Path.Combine(_storageRoot, "exports", jobId.ToString("D"));
        Directory.CreateDirectory(directoryPath);

        var result = await _provider.DeleteAsync(jobId, JobDirectoryType.Export);

        Assert.Equal(JobDirectoryOperationStatus.Conflict, result.Status);
        Assert.True(Directory.Exists(directoryPath));
    }

    [Fact]
    public async Task DeleteAsync_DoesNotDeleteWhenMarkerMismatch()
    {
        var jobId = Guid.Parse("ef3a2b1c-0000-0000-0000-000000000008");
        var directoryPath = Path.Combine(_storageRoot, "exports", jobId.ToString("D"));
        Directory.CreateDirectory(directoryPath);
        await File.WriteAllTextAsync(
            Path.Combine(directoryPath, LinuxJobDirectoryProvider.MarkerFileName),
            """{"jobId":"00000000-0000-0000-0000-000000000099","jobType":"Export","createdAtUtc":"2026-01-01T00:00:00Z","agentVersion":"1.0.0"}""");

        var result = await _provider.DeleteAsync(jobId, JobDirectoryType.Export);

        Assert.Equal(JobDirectoryOperationStatus.Conflict, result.Status);
        Assert.True(Directory.Exists(directoryPath));
    }

    [Fact]
    public void IsProtectedDirectory_BlocksExportsAndImportsRoots()
    {
        var exportsRoot = Path.Combine(_storageRoot, "exports");
        var importsRoot = Path.Combine(_storageRoot, "imports");

        Assert.True(_provider.IsProtectedDirectory(_storageRoot));
        Assert.True(_provider.IsProtectedDirectory(exportsRoot));
        Assert.True(_provider.IsProtectedDirectory(importsRoot));
    }

    [Fact]
    public async Task DeleteAsync_DoesNotDeleteNonEmptyDirectory()
    {
        var jobId = Guid.Parse("ff3a2b1c-0000-0000-0000-000000000009");
        var createResult = await _provider.CreateAsync(jobId, JobDirectoryType.Export);
        await File.WriteAllTextAsync(Path.Combine(createResult.Data!.PhysicalPath, "payload.pst"), "data");

        var result = await _provider.DeleteAsync(jobId, JobDirectoryType.Export);

        Assert.Equal(JobDirectoryOperationStatus.Conflict, result.Status);
        Assert.True(Directory.Exists(createResult.Data.PhysicalPath));
    }

    [Fact]
    public async Task DeleteAsync_DeletesMarkerOnlyDirectory()
    {
        var jobId = Guid.Parse("1f3a2b1c-0000-0000-0000-000000000010");
        var createResult = await _provider.CreateAsync(jobId, JobDirectoryType.Import);
        var physicalPath = createResult.Data!.PhysicalPath;

        var result = await _provider.DeleteAsync(jobId, JobDirectoryType.Import);

        Assert.Equal(JobDirectoryOperationStatus.Success, result.Status);
        Assert.True(result.Data!.Deleted);
        Assert.False(Directory.Exists(physicalPath));
    }

    [Fact]
    public async Task CreateAsync_RejectsWhenStorageNotMounted()
    {
        _mountChecker.IsMounted = false;
        var jobId = Guid.Parse("2f3a2b1c-0000-0000-0000-000000000011");

        var result = await _provider.CreateAsync(jobId, JobDirectoryType.Export);

        Assert.Equal(JobDirectoryOperationStatus.ServiceUnavailable, result.Status);
    }

    [Fact]
    public async Task GetAsync_ReturnsAggregateFileStats()
    {
        var jobId = Guid.Parse("3f3a2b1c-0000-0000-0000-000000000012");
        var createResult = await _provider.CreateAsync(jobId, JobDirectoryType.Export);
        await File.WriteAllTextAsync(Path.Combine(createResult.Data!.PhysicalPath, "sample.txt"), "12345");

        var result = await _provider.GetAsync(jobId, JobDirectoryType.Export);

        Assert.Equal(JobDirectoryOperationStatus.Success, result.Status);
        Assert.True(result.Data!.Exists);
        Assert.Equal(2, result.Data.TotalFileCount);
        Assert.True(result.Data.TotalFileSizeBytes >= 5);
    }

    [Fact]
    public async Task GetAsync_ReturnsNotFoundForMissingDirectory()
    {
        var jobId = Guid.Parse("4f3a2b1c-0000-0000-0000-000000000013");

        var result = await _provider.GetAsync(jobId, JobDirectoryType.Export);

        Assert.Equal(JobDirectoryOperationStatus.NotFound, result.Status);
    }

    public void Dispose()
    {
        if (Directory.Exists(_storageRoot))
        {
            Directory.Delete(_storageRoot, recursive: true);
        }
    }

    [Fact]
    public async Task CreateAsync_CallsExchangeAclService()
    {
        var aclService = new FakeExchangeAclService();
        var provider = CreateProvider(_storageRoot, _mountChecker, aclService);
        var jobId = Guid.Parse("5f3a2b1c-0000-0000-0000-000000000014");

        await provider.CreateAsync(jobId, JobDirectoryType.Export);

        Assert.Single(aclService.Calls);
        Assert.Contains(_storageRoot, aclService.Calls[0].Path, StringComparison.Ordinal);
    }

    private static LinuxJobDirectoryProvider CreateProvider(
        string storageRoot,
        FakeStorageMountChecker mountChecker,
        FakeExchangeAclService? aclService = null) =>
        CreateProvider(storageRoot, mountChecker, (IExchangeAclService)(aclService ?? new FakeExchangeAclService()));

    private static LinuxJobDirectoryProvider CreateProvider(
        string storageRoot,
        FakeStorageMountChecker mountChecker,
        IExchangeAclService exchangeAclService) =>
        new(
            Microsoft.Extensions.Options.Options.Create(new TransferAgentOptions
            {
                StorageRootPath = storageRoot,
                StorageDirectoryMode = "750"
            }),
            Microsoft.Extensions.Options.Options.Create(new SambaOptions
            {
                Enabled = false,
                StorageRoot = storageRoot,
                RequiredAdGroup = @"DOGRU\Exchange Trusted Subsystem"
            }),
            mountChecker,
            new SambaPathService(
                Microsoft.Extensions.Options.Options.Create(new SambaOptions
                {
                    Enabled = false,
                    StorageRoot = storageRoot
                }),
                Microsoft.Extensions.Options.Options.Create(new TransferAgentOptions
                {
                    StorageRootPath = storageRoot
                }),
                new ExcPanel.TransferAgent.Tests.Fakes.FakeSetupConfigStore()),
            exchangeAclService,
            NullLogger<LinuxJobDirectoryProvider>.Instance);
}
