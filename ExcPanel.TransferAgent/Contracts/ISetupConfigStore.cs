namespace ExcPanel.TransferAgent.Contracts;

public interface ISetupConfigStore
{
    Task<Models.Setup.SetupConfigDocument> GetAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(Models.Setup.SetupConfigDocument config, CancellationToken cancellationToken = default);
}
