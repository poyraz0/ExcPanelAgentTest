namespace ExcPanel.TransferAgent.Contracts;

public interface IExchangeAclService
{
    Task<ExchangeAclResult> ApplyExchangeAclAsync(
        string jobDirectoryPath,
        string requiredAdGroup,
        CancellationToken cancellationToken = default);
}

public class ExchangeAclResult
{
    public bool Success { get; init; }
    public string? Message { get; init; }
}
