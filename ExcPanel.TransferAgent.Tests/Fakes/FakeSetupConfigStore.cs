using ExcPanel.TransferAgent.Contracts;
using ExcPanel.TransferAgent.Models.Setup;

namespace ExcPanel.TransferAgent.Tests.Fakes;

internal sealed class FakeSetupConfigStore : ISetupConfigStore
{
    public SetupConfigDocument Document { get; set; } = new();

    public Task<SetupConfigDocument> GetAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Document);

    public Task SaveAsync(SetupConfigDocument config, CancellationToken cancellationToken = default)
    {
        Document = config;
        return Task.CompletedTask;
    }
}
