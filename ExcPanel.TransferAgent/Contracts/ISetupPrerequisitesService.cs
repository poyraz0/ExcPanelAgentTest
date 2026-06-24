using ExcPanel.TransferAgent.Models.Setup;

namespace ExcPanel.TransferAgent.Contracts;

public interface ISetupPrerequisitesService
{
    Task<SetupPrerequisitesResponse> CheckAsync(CancellationToken cancellationToken = default);
}
