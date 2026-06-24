using ExcPanel.TransferAgent.Contracts;

namespace ExcPanel.TransferAgent.Services.Sftp;

public class DefaultSftpAuthorizationService : ISftpAuthorizationService
{
    public Task<bool> AuthorizeAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(true);
}
