using ExcPanel.TransferAgent.Contracts;
using ExcPanel.TransferAgent.Models;

namespace ExcPanel.TransferAgent.Providers.Windows;

public class WindowsSmbProvider : ISambaProvider
{
    public Task<SambaStatusResponse> GetStatusAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new SambaStatusResponse
        {
            Enabled = false,
            Supported = false,
            Platform = "Windows",
            ValidationErrors = ["Samba provider is not implemented on Windows."]
        });

    public Task<SambaPrerequisitesResponse> GetPrerequisitesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new SambaPrerequisitesResponse
        {
            Supported = false,
            Ready = false,
            Items =
            [
                new SambaPrerequisiteItem
                {
                    Name = "platform",
                    Satisfied = false,
                    Message = "Samba operations are not supported on Windows in this version.",
                    RecommendedActions = ["Use a Linux transfer agent host for Samba share configuration."]
                }
            ]
        });

    public Task<SambaInitializeDryRunResponse> BuildConfigurationPlanAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new SambaInitializeDryRunResponse
        {
            Supported = false,
            Valid = false,
            ValidationErrors = ["Samba configuration is not supported on Windows."]
        });

    public Task<SambaOperationResult<SambaInitializeResponse>> InitializeAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(SambaOperationResult<SambaInitializeResponse>.Fail(
            SambaOperationStatus.NotSupported,
            "Samba initialization is not supported on Windows."));

    public Task<(bool Valid, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings)> ValidateConfigurationAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult<(bool, IReadOnlyList<string>, IReadOnlyList<string>)>(
            (false, ["Samba validation is not supported on Windows."], Array.Empty<string>()));

    public SambaOperationResult<SambaUncPathResponse> BuildUncPath(Guid jobId, JobDirectoryType jobType) =>
        SambaOperationResult<SambaUncPathResponse>.Fail(
            SambaOperationStatus.NotSupported,
            "UNC path generation is not supported on Windows.");

    public Task<bool> TestShareAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(false);
}
