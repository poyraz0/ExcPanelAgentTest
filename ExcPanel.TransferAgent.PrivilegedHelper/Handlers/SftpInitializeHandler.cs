using System.Text.Json;
using ExcPanel.TransferAgent.Contracts;
using ExcPanel.TransferAgent.PrivilegedHelper.Commands;

namespace ExcPanel.TransferAgent.PrivilegedHelper.Handlers;

public class SftpInitializeHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IPrivilegedCommandRunner _commandRunner;

    public SftpInitializeHandler(IPrivilegedCommandRunner commandRunner)
    {
        _commandRunner = commandRunner;
    }

    public async Task<PrivilegedHelperResponse> HandleInitializeAsync(
        string requestId,
        JsonElement payloadElement,
        CancellationToken cancellationToken)
    {
        var payload = DeserializePayload<SftpInitializePayload>(payloadElement);
        if (payload is null)
        {
            return InvalidArguments(requestId, "sftp.initialize payload is invalid.");
        }

        var completedSteps = new List<string>();
        var exportGroupCreated = false;
        var importGroupCreated = false;
        var chrootBaseCreated = false;
        var sshdInstalled = false;
        var sshdReloaded = false;

        if (!UnixIdentity.ExistsGroup(payload.ExportGroupName))
        {
            var result = await _commandRunner.RunAsync(
                "groupadd",
                [payload.ExportGroupName],
                cancellationToken: cancellationToken);
            if (result.ExitCode != 0)
            {
                return FailureFromCommand(requestId, "CreateExportGroup", result, completedSteps);
            }

            exportGroupCreated = true;
        }

        completedSteps.Add("EnsureExportGroup");

        if (!UnixIdentity.ExistsGroup(payload.ImportGroupName))
        {
            var result = await _commandRunner.RunAsync(
                "groupadd",
                [payload.ImportGroupName],
                cancellationToken: cancellationToken);
            if (result.ExitCode != 0)
            {
                return FailureFromCommand(requestId, "CreateImportGroup", result, completedSteps);
            }

            importGroupCreated = true;
        }

        completedSteps.Add("EnsureImportGroup");

        Directory.CreateDirectory(payload.ChrootBasePath);
        await _commandRunner.RunAsync("chown", ["root:root", payload.ChrootBasePath], cancellationToken: cancellationToken);
        await _commandRunner.RunAsync("chmod", ["0755", payload.ChrootBasePath], cancellationToken: cancellationToken);
        await SftpHostMountHelper.EnsureSharedMountPropagationAsync(
            _commandRunner,
            payload.ChrootBasePath,
            cancellationToken);
        chrootBaseCreated = true;
        completedSteps.Add("EnsureChrootBase");

        var dropInContent = SshdConfigManager.BuildDropInContent(
            payload.ExportGroupName,
            payload.ImportGroupName,
            payload.ChrootBasePath.TrimEnd('/'));

        var installResult = await SshdConfigManager.InstallDropInAsync(
            payload.SshdDropInPath,
            dropInContent,
            _commandRunner,
            cancellationToken);
        if (!installResult.Success)
        {
            return PrivilegedHelperResponse.Failure(
                requestId,
                PrivilegedHelperErrorCodes.SshdValidationFailed,
                "Failed to install sshd drop-in configuration.",
                failedStep: "InstallSshdDropIn",
                completedSteps: completedSteps,
                stderr: installResult.ErrorMessage);
        }

        sshdInstalled = true;
        sshdReloaded = true;
        completedSteps.Add("InstallSshdDropIn");

        var data = JsonSerializer.SerializeToElement(new SftpInitializeResultData
        {
            Initialized = true,
            ExportGroupCreated = exportGroupCreated,
            ImportGroupCreated = importGroupCreated,
            ChrootBaseCreated = chrootBaseCreated,
            SshdDropInInstalled = sshdInstalled,
            SshdReloaded = sshdReloaded,
            ChrootBasePath = payload.ChrootBasePath,
            SshdDropInPath = payload.SshdDropInPath
        }, JsonOptions);

        return PrivilegedHelperResponse.Ok(
            requestId,
            "SFTP initialized successfully.",
            completedSteps,
            data);
    }

    public Task<PrivilegedHelperResponse> HandleStatusAsync(
        string requestId,
        JsonElement payloadElement,
        CancellationToken cancellationToken)
    {
        var payload = DeserializePayload<SftpInitializePayload>(payloadElement);
        if (payload is null)
        {
            return Task.FromResult(InvalidArguments(requestId, "sftp status payload is invalid."));
        }

        var initialized = UnixIdentity.ExistsGroup(payload.ExportGroupName)
            && UnixIdentity.ExistsGroup(payload.ImportGroupName)
            && Directory.Exists(payload.ChrootBasePath)
            && File.Exists(payload.SshdDropInPath);

        var statusData = JsonSerializer.SerializeToElement(new SftpStatusResultData
        {
            Initialized = initialized,
            ExportGroupExists = UnixIdentity.ExistsGroup(payload.ExportGroupName),
            ImportGroupExists = UnixIdentity.ExistsGroup(payload.ImportGroupName),
            ChrootBaseExists = Directory.Exists(payload.ChrootBasePath),
            SshdDropInExists = File.Exists(payload.SshdDropInPath),
            ChrootBasePath = payload.ChrootBasePath,
            SshdDropInPath = payload.SshdDropInPath
        }, JsonOptions);

        return Task.FromResult(PrivilegedHelperResponse.Ok(
            requestId,
            "SFTP status retrieved.",
            Array.Empty<string>(),
            statusData));
    }

    private static T? DeserializePayload<T>(JsonElement payloadElement)
    {
        try
        {
            return payloadElement.Deserialize<T>(JsonOptions);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static PrivilegedHelperResponse InvalidArguments(string requestId, string message) =>
        PrivilegedHelperResponse.Failure(requestId, PrivilegedHelperErrorCodes.InvalidArguments, message);

    private static PrivilegedHelperResponse FailureFromCommand(
        string requestId,
        string failedStep,
        CommandExecutionResult result,
        IReadOnlyList<string> completedSteps) =>
        PrivilegedHelperResponse.Failure(
            requestId,
            PrivilegedHelperErrorCodes.CommandFailed,
            $"Command '{result.Executable}' failed during step '{failedStep}'.",
            failedStep: failedStep,
            completedSteps: completedSteps,
            exitCode: result.ExitCode,
            stderr: result.Stderr);
}
