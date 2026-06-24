using ExcPanel.TransferAgent.Models;

namespace ExcPanel.TransferAgent.Contracts;

public interface ISambaProvider
{
    Task<SambaStatusResponse> GetStatusAsync(CancellationToken cancellationToken = default);

    Task<SambaPrerequisitesResponse> GetPrerequisitesAsync(CancellationToken cancellationToken = default);

    Task<SambaInitializeDryRunResponse> BuildConfigurationPlanAsync(CancellationToken cancellationToken = default);

    Task<SambaOperationResult<SambaInitializeResponse>> InitializeAsync(CancellationToken cancellationToken = default);

    Task<(bool Valid, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings)> ValidateConfigurationAsync(
        CancellationToken cancellationToken = default);

    SambaOperationResult<SambaUncPathResponse> BuildUncPath(Guid jobId, JobDirectoryType jobType);

    Task<bool> TestShareAsync(CancellationToken cancellationToken = default);
}
