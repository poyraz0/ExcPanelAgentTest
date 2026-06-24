using ExcPanel.TransferAgent.Models.Domain;

namespace ExcPanel.TransferAgent.Contracts;

public interface IDomainService
{
    Task<DomainStatusResponse> GetStatusAsync(CancellationToken cancellationToken = default);
    Task<DomainPrecheckResponse> PrecheckAsync(DomainPrecheckRequest request, CancellationToken cancellationToken = default);
    Task<DomainJoinResponse> JoinAsync(DomainJoinRequest request, CancellationToken cancellationToken = default);
    Task<DomainTestResponse> TestAsync(CancellationToken cancellationToken = default);
}
