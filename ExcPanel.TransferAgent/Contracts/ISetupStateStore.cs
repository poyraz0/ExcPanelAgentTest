namespace ExcPanel.TransferAgent.Contracts;

public interface ISetupStateStore
{
    Task<Models.Setup.SetupState> GetAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(Models.Setup.SetupState state, CancellationToken cancellationToken = default);
}
