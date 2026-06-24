namespace ExcPanel.TransferAgent.Contracts;

public interface ISftpAuthorizationService
{
    Task<bool> AuthorizeAsync(CancellationToken cancellationToken = default);
}
