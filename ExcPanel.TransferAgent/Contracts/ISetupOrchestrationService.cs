using ExcPanel.TransferAgent.Models.Setup;

namespace ExcPanel.TransferAgent.Contracts;

public interface ISetupOrchestrationService
{
    Task<SetupStatusResponse> GetStatusAsync(CancellationToken cancellationToken = default);
    Task<SetupPlanResponse> PlanAsync(SetupPlanRequest request, CancellationToken cancellationToken = default);
    Task<SetupApplyResponse> ApplyAsync(SetupApplyRequest request, CancellationToken cancellationToken = default);
    Task<SetupSambaWriteTestResponse> TestSambaWriteAsync(CancellationToken cancellationToken = default);
}
