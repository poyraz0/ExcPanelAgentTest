using ExcPanel.TransferAgent.Contracts;

namespace ExcPanel.TransferAgent.Tests.Fakes;

public sealed class FakeStorageMountChecker : IStorageMountChecker
{
    public bool IsMounted { get; set; } = true;

    public Task<bool> IsMountedAsync(string path, CancellationToken cancellationToken = default) =>
        Task.FromResult(IsMounted);
}
