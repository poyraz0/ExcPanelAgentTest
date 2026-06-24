using ExcPanel.TransferAgent.Models;

namespace ExcPanel.TransferAgent.Contracts;

public interface ISftpService
{
    Task<SftpOperationResult<SftpInitializeResponse>> InitializeAsync(CancellationToken cancellationToken = default);
    Task<SftpOperationResult<SftpStatusResponse>> GetStatusAsync(CancellationToken cancellationToken = default);
    Task<SftpOperationResult<SftpUserCredentialsResponse>> CreateUserAsync(CreateSftpUserRequest request, CancellationToken cancellationToken = default);
    Task<SftpOperationResult<SftpUserInfoResponse>> GetUserAsync(string username, CancellationToken cancellationToken = default);
    Task<SftpOperationResult<SftpDisableUserResponse>> DisableUserAsync(string username, CancellationToken cancellationToken = default);
    Task<SftpOperationResult<SftpDeleteUserResponse>> DeleteUserAsync(string username, CancellationToken cancellationToken = default);
    Task ReconcileExpiredUsersAsync(CancellationToken cancellationToken = default);
    Task ReconcileBindMountsAsync(CancellationToken cancellationToken = default);
}
