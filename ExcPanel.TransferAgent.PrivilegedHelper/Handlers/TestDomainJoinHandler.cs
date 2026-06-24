using System.Text.Json;
using ExcPanel.TransferAgent.Contracts;
using ExcPanel.TransferAgent.PrivilegedHelper.Commands;

namespace ExcPanel.TransferAgent.PrivilegedHelper.Handlers;

public class TestDomainJoinHandler
{
    private static readonly string[] FixedArguments = ["ads", "testjoin"];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IPrivilegedCommandRunner _commandRunner;

    public TestDomainJoinHandler(IPrivilegedCommandRunner commandRunner)
    {
        _commandRunner = commandRunner;
    }

    public async Task<PrivilegedHelperResponse> HandleAsync(
        string requestId,
        CancellationToken cancellationToken)
    {
        if (!_commandRunner.ResolvedExecutables.TryGetValue("net", out var netExecutable) ||
            !string.Equals(netExecutable, "/usr/bin/net", StringComparison.Ordinal))
        {
            return PrivilegedHelperResponse.Failure(
                requestId,
                PrivilegedHelperErrorCodes.CommandFailed,
                "Required executable '/usr/bin/net' is not available.",
                failedStep: "TestDomainJoin");
        }

        var result = await _commandRunner.RunAsync(
            "net",
            FixedArguments,
            cancellationToken: cancellationToken);

        var data = JsonSerializer.SerializeToElement(new TestDomainJoinResultData
        {
            DomainJoined = result.ExitCode == 0,
            Stderr = result.ExitCode == 0 ? null : result.Stderr
        }, JsonOptions);

        return PrivilegedHelperResponse.Ok(
            requestId,
            result.ExitCode == 0
                ? "Domain join verification succeeded."
                : "Domain join verification failed.",
            ["TestDomainJoin"],
            data);
    }
}
