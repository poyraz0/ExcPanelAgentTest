using System.Text.Json;
using ExcPanel.TransferAgent.Contracts;
using ExcPanel.TransferAgent.Contracts.Validation;
using ExcPanel.TransferAgent.PrivilegedHelper.Commands;

namespace ExcPanel.TransferAgent.PrivilegedHelper.Handlers;

public class StorageRemountHandler
{
    private readonly IPrivilegedCommandRunner _commandRunner;

    public StorageRemountHandler(IPrivilegedCommandRunner commandRunner)
    {
        _commandRunner = commandRunner;
    }

    public async Task<PrivilegedHelperResponse> HandleAsync(
        string requestId,
        JsonElement payload,
        CancellationToken cancellationToken)
    {
        var remountPayload = payload.Deserialize<StorageRemountPayload>(PrivilegedHelperJson.SerializerOptions);
        if (remountPayload is null || string.IsNullOrWhiteSpace(remountPayload.MountPath))
        {
            return PrivilegedHelperResponse.Failure(
                requestId,
                PrivilegedHelperErrorCodes.InvalidArguments,
                "mountPath is required.");
        }

        var mountPath = remountPayload.MountPath.Trim();
        var mountErrors = StorageValidationHelpers.ValidateMountPath(mountPath, allowExistingContent: true);
        if (mountErrors.Count > 0)
        {
            return PrivilegedHelperResponse.Failure(
                requestId,
                PrivilegedHelperErrorCodes.InvalidArguments,
                mountErrors[0]);
        }

        var completedSteps = new List<string>();

        if (await IsMountedAsync(mountPath, cancellationToken))
        {
            completedSteps.Add("AlreadyMounted");
            return PrivilegedHelperResponse.Ok(
                requestId,
                "Storage path is already mounted.",
                completedSteps,
                JsonSerializer.SerializeToElement(
                    new StorageRemountResultData { MountPath = mountPath, Mounted = true },
                    PrivilegedHelperJson.SerializerOptions));
        }

        if (!await FstabContainsMountPathAsync(mountPath, cancellationToken))
        {
            return PrivilegedHelperResponse.Failure(
                requestId,
                PrivilegedHelperErrorCodes.InvalidArguments,
                $"No fstab entry found for '{mountPath}'.");
        }

        Directory.CreateDirectory(mountPath);
        completedSteps.Add("CreateMountDirectory");

        var mountResult = await _commandRunner.RunAsync(
            "mount",
            new[] { mountPath },
            cancellationToken: cancellationToken);

        if (mountResult.ExitCode != 0)
        {
            return FailureFromCommand(requestId, "MountStoragePath", mountResult, completedSteps);
        }

        completedSteps.Add("MountStoragePath");

        if (!await IsMountedAsync(mountPath, cancellationToken))
        {
            return PrivilegedHelperResponse.Failure(
                requestId,
                PrivilegedHelperErrorCodes.StorageNotMounted,
                $"Mount path '{mountPath}' is not mounted after remount attempt.",
                completedSteps: completedSteps);
        }

        completedSteps.Add("VerifyMount");
        return PrivilegedHelperResponse.Ok(
            requestId,
            "Storage remounted successfully.",
            completedSteps,
            JsonSerializer.SerializeToElement(
                new StorageRemountResultData { MountPath = mountPath, Mounted = true },
                PrivilegedHelperJson.SerializerOptions));
    }

    private static async Task<bool> FstabContainsMountPathAsync(string mountPath, CancellationToken cancellationToken)
    {
        if (!File.Exists("/etc/fstab"))
        {
            return false;
        }

        var fstabContent = await File.ReadAllTextAsync("/etc/fstab", cancellationToken);
        return fstabContent
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(line => !line.StartsWith('#') && line.Contains(mountPath, StringComparison.Ordinal));
    }

    private async Task<bool> IsMountedAsync(string mountPath, CancellationToken cancellationToken)
    {
        var result = await _commandRunner.RunAsync(
            "findmnt",
            new[] { "-n", "-M", mountPath },
            cancellationToken: cancellationToken);

        return result.ExitCode == 0;
    }

    private static PrivilegedHelperResponse FailureFromCommand(
        string requestId,
        string step,
        CommandExecutionResult result,
        List<string> completedSteps) =>
        PrivilegedHelperResponse.Failure(
            requestId,
            PrivilegedHelperErrorCodes.CommandFailed,
            $"Step '{step}' failed.",
            failedStep: step,
            completedSteps: completedSteps,
            exitCode: result.ExitCode,
            stderr: result.Stderr);
}
