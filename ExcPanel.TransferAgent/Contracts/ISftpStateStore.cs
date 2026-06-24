using ExcPanel.TransferAgent.Models;

namespace ExcPanel.TransferAgent.Contracts;

public interface ISftpStateStore
{
    Task<SftpStateDocument> GetAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(SftpStateDocument state, CancellationToken cancellationToken = default);
    Task<SftpUserRecord?> FindByUsernameAsync(string username, CancellationToken cancellationToken = default);
    Task<SftpUserRecord?> FindByJobAsync(Guid jobId, JobDirectoryType jobType, CancellationToken cancellationToken = default);
    Task UpsertUserAsync(SftpUserRecord record, CancellationToken cancellationToken = default);
    Task RemoveUserAsync(string username, CancellationToken cancellationToken = default);
    Task SetInitializedAsync(bool initialized, CancellationToken cancellationToken = default);
}
