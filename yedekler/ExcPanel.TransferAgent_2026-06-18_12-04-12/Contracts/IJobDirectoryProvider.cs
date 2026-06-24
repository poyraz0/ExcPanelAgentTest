using ExcPanel.TransferAgent.Models;

namespace ExcPanel.TransferAgent.Contracts;

public interface IJobDirectoryProvider
{
    Task<JobDirectoryOperationResult<JobDirectoryCreateResponse>> CreateAsync(
        Guid jobId,
        JobDirectoryType jobType,
        CancellationToken cancellationToken = default);

    Task<JobDirectoryOperationResult<JobDirectoryInfoResponse>> GetAsync(
        Guid jobId,
        JobDirectoryType jobType,
        CancellationToken cancellationToken = default);

    Task<JobDirectoryOperationResult<JobDirectoryDeleteResponse>> DeleteAsync(
        Guid jobId,
        JobDirectoryType jobType,
        CancellationToken cancellationToken = default);
}
