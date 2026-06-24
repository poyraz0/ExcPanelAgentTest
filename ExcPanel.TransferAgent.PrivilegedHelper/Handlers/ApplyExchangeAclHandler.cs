using System.Text.Json;
using ExcPanel.TransferAgent.Contracts;
using ExcPanel.TransferAgent.Contracts.Validation;
using ExcPanel.TransferAgent.PrivilegedHelper.Commands;

namespace ExcPanel.TransferAgent.PrivilegedHelper.Handlers;

public class ApplyExchangeAclHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IPrivilegedCommandRunner _commandRunner;

    public ApplyExchangeAclHandler(IPrivilegedCommandRunner commandRunner)
    {
        _commandRunner = commandRunner;
    }

    public async Task<PrivilegedHelperResponse> HandleAsync(
        string requestId,
        JsonElement payloadElement,
        CancellationToken cancellationToken)
    {
        ApplyExchangeAclPayload payload;
        try
        {
            payload = payloadElement.Deserialize<ApplyExchangeAclPayload>(JsonOptions)
                ?? throw new JsonException("Payload is null.");
        }
        catch (JsonException)
        {
            return PrivilegedHelperResponse.Failure(
                requestId,
                PrivilegedHelperErrorCodes.InvalidArguments,
                "permissions.applyExchangeAcl payload is invalid.");
        }

        if (string.IsNullOrWhiteSpace(payload.JobDirectoryPath) ||
            string.IsNullOrWhiteSpace(payload.StorageRootPath) ||
            string.IsNullOrWhiteSpace(payload.RequiredAdGroup))
        {
            return PrivilegedHelperResponse.Failure(
                requestId,
                PrivilegedHelperErrorCodes.InvalidArguments,
                "jobDirectoryPath, storageRootPath, and requiredAdGroup are required.");
        }

        string normalizedRoot;
        string normalizedJob;
        try
        {
            normalizedRoot = Path.GetFullPath(payload.StorageRootPath.Trim())
                .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            normalizedJob = Path.GetFullPath(payload.JobDirectoryPath.Trim());
        }
        catch
        {
            return PrivilegedHelperResponse.Failure(
                requestId,
                PrivilegedHelperErrorCodes.InvalidJobPath,
                "Job directory path could not be resolved.");
        }

        if (!normalizedJob.StartsWith(normalizedRoot, StringComparison.Ordinal))
        {
            return PrivilegedHelperResponse.Failure(
                requestId,
                PrivilegedHelperErrorCodes.InvalidJobPath,
                "Job directory path is outside the storage root.");
        }

        if (!Directory.Exists(normalizedJob))
        {
            return PrivilegedHelperResponse.Failure(
                requestId,
                PrivilegedHelperErrorCodes.InvalidJobPath,
                "Job directory does not exist.");
        }

        var completedSteps = new List<string>();
        var group = payload.RequiredAdGroup.Trim();

        var setAcl = await _commandRunner.RunAsync(
            "setfacl",
            ["-m", $"g:{group}:rwx", normalizedJob],
            cancellationToken: cancellationToken);
        if (setAcl.ExitCode != 0)
        {
            return FailureFromCommand(requestId, "ApplyJobAcl", setAcl, completedSteps);
        }

        var setDefaultAcl = await _commandRunner.RunAsync(
            "setfacl",
            ["-d", "-m", $"g:{group}:rwx", normalizedJob],
            cancellationToken: cancellationToken);
        if (setDefaultAcl.ExitCode != 0)
        {
            return FailureFromCommand(requestId, "ApplyDefaultJobAcl", setDefaultAcl, completedSteps);
        }

        completedSteps.Add("ApplyExchangeAcl");

        var data = JsonSerializer.SerializeToElement(new ApplyExchangeAclResultData
        {
            Applied = true,
            JobDirectoryPath = normalizedJob
        }, JsonOptions);

        return PrivilegedHelperResponse.Ok(
            requestId,
            "Exchange ACL applied successfully.",
            completedSteps,
            data);
    }

    private static PrivilegedHelperResponse FailureFromCommand(
        string requestId,
        string failedStep,
        CommandExecutionResult result,
        List<string> completedSteps) =>
        PrivilegedHelperResponse.Failure(
            requestId,
            PrivilegedHelperErrorCodes.CommandFailed,
            $"Exchange ACL application failed at step '{failedStep}'.",
            failedStep: failedStep,
            completedSteps: completedSteps,
            exitCode: result.ExitCode,
            stderr: result.Stderr);
}
