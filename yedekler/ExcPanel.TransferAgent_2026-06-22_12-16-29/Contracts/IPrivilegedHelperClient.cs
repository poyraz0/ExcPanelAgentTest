namespace ExcPanel.TransferAgent.Contracts;

public interface IPrivilegedHelperClient
{
    Task<PrivilegedHelperResponse> InvokeAsync(
        PrivilegedHelperRequest request,
        CancellationToken cancellationToken = default);
}
