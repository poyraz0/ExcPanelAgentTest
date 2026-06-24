using System.Text.Json;
using ExcPanel.TransferAgent.Contracts;
using ExcPanel.TransferAgent.Options;
using Microsoft.Extensions.Options;

namespace ExcPanel.TransferAgent.Services.PrivilegedHelper;

public class ExchangeAclService : IExchangeAclService
{
    private readonly IPrivilegedHelperClient _privilegedHelperClient;
    private readonly TransferAgentOptions _options;

    public ExchangeAclService(
        IPrivilegedHelperClient privilegedHelperClient,
        IOptions<TransferAgentOptions> options)
    {
        _privilegedHelperClient = privilegedHelperClient;
        _options = options.Value;
    }

    public async Task<ExchangeAclResult> ApplyExchangeAclAsync(
        string jobDirectoryPath,
        string requiredAdGroup,
        CancellationToken cancellationToken = default)
    {
        var request = new PrivilegedHelperRequest
        {
            RequestId = Guid.NewGuid().ToString("D"),
            Action = PrivilegedHelperActions.PermissionsApplyExchangeAcl,
            Payload = JsonSerializer.SerializeToElement(new ApplyExchangeAclPayload
            {
                JobDirectoryPath = jobDirectoryPath,
                StorageRootPath = _options.StorageRootPath,
                RequiredAdGroup = requiredAdGroup
            }, PrivilegedHelperJson.SerializerOptions)
        };

        var response = await _privilegedHelperClient.InvokeAsync(request, cancellationToken);
        if (!response.Success)
        {
            return new ExchangeAclResult
            {
                Success = false,
                Message = response.Message
            };
        }

        return new ExchangeAclResult { Success = true };
    }
}
