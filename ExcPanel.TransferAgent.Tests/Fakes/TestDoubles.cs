using ExcPanel.TransferAgent.Contracts;
using ExcPanel.TransferAgent.Models;
using ExcPanel.TransferAgent.Services.Storage;

namespace ExcPanel.TransferAgent.Tests.Fakes;

public sealed class FakePrivilegedHelperClient : IPrivilegedHelperClient
{
    public Func<PrivilegedHelperRequest, PrivilegedHelperResponse>? Handler { get; set; }

    public Task<PrivilegedHelperResponse> InvokeAsync(
        PrivilegedHelperRequest request,
        CancellationToken cancellationToken = default)
    {
        if (Handler is null)
        {
            return Task.FromResult(PrivilegedHelperResponse.Failure(
                request.RequestId,
                "TEST_NOT_CONFIGURED",
                "Fake helper client was not configured."));
        }

        return Task.FromResult(Handler(request));
    }
}

public sealed class FakeDiskDiscoveryService : IStorageDiskDiscoveryService
{
    public List<StorageDiskInfo> Disks { get; set; } = [];

    public Task<StorageDiskDiscoveryResponse> DiscoverDisksAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new StorageDiskDiscoveryResponse
        {
            Supported = true,
            Platform = "Linux",
            Disks = Disks
        });
}

public sealed class FakeExchangeAclService : IExchangeAclService
{
    public List<(string Path, string Group)> Calls { get; } = [];

    public Task<ExchangeAclResult> ApplyExchangeAclAsync(
        string jobDirectoryPath,
        string requiredAdGroup,
        CancellationToken cancellationToken = default)
    {
        Calls.Add((jobDirectoryPath, requiredAdGroup));
        return Task.FromResult(new ExchangeAclResult { Success = true });
    }
}

public static class TestDiskFactory
{
    public static StorageDiskInfo CreateRecommendedDisk(
        string path = "/dev/sdb",
        long sizeBytes = 21474836480,
        string serial = "DISK-SERIAL-001",
        string? wwn = null) =>
        new()
        {
            Name = path.TrimStart('/'),
            Path = path,
            SizeBytes = sizeBytes,
            SizeGb = sizeBytes / 1024d / 1024d / 1024d,
            Serial = serial,
            Wwn = wwn,
            Type = "disk",
            IsMounted = false,
            HasPartitions = false,
            IsSystemDisk = false,
            Recommended = true
        };
}
