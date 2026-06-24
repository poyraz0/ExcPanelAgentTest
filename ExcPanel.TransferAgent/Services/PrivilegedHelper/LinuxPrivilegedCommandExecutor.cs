using System.Text.Json;
using ExcPanel.TransferAgent.Contracts;

namespace ExcPanel.TransferAgent.Services.PrivilegedHelper;

public class LinuxPrivilegedCommandExecutor : IPrivilegedCommandExecutor
{
    private readonly IPrivilegedHelperClient _privilegedHelperClient;

    public LinuxPrivilegedCommandExecutor(IPrivilegedHelperClient privilegedHelperClient)
    {
        _privilegedHelperClient = privilegedHelperClient;
    }

    public async Task<TestDomainJoinResult> TestDomainJoinAsync(CancellationToken cancellationToken = default)
    {
        var request = new PrivilegedHelperRequest
        {
            RequestId = Guid.NewGuid().ToString("D"),
            Action = PrivilegedHelperActions.TestDomainJoin,
            Payload = JsonDocument.Parse("{}").RootElement.Clone()
        };

        var response = await _privilegedHelperClient.InvokeAsync(request, cancellationToken);
        if (response.Success)
        {
            var data = response.Data.Deserialize<TestDomainJoinResultData>(PrivilegedHelperJson.SerializerOptions);
            return new TestDomainJoinResult
            {
                DomainJoined = data?.DomainJoined ?? false,
                Stderr = data?.Stderr
            };
        }

        if (IsHelperInfrastructureFailure(response.ErrorCode))
        {
            return new TestDomainJoinResult
            {
                DomainJoined = false,
                ErrorMessage = response.Message
            };
        }

        var failureData = response.Data.Deserialize<TestDomainJoinResultData>(PrivilegedHelperJson.SerializerOptions);
        return new TestDomainJoinResult
        {
            DomainJoined = failureData?.DomainJoined ?? false,
            Stderr = failureData?.Stderr ?? response.FailedCommandStderr,
            ErrorMessage = response.Message
        };
    }

    private static bool IsHelperInfrastructureFailure(string? errorCode) =>
        errorCode is "HELPER_NOT_FOUND" or "HELPER_NOT_AUTHORIZED" or "HELPER_START_FAILED" or "HELPER_TIMEOUT";
}
