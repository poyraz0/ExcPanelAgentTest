namespace ExcPanel.TransferAgent.Contracts;

public interface IPrivilegedCommandExecutor
{
    Task<TestDomainJoinResult> TestDomainJoinAsync(CancellationToken cancellationToken = default);
}

public class TestDomainJoinResult
{
    public bool DomainJoined { get; init; }
    public string? Stderr { get; init; }
    public string? ErrorMessage { get; init; }
}
