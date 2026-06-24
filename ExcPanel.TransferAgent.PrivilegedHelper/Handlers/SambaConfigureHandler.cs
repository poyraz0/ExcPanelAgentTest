using System.Text.Json;
using ExcPanel.TransferAgent.Contracts;
using ExcPanel.TransferAgent.Contracts.Validation;
using ExcPanel.TransferAgent.PrivilegedHelper.Commands;

namespace ExcPanel.TransferAgent.PrivilegedHelper.Handlers;

public class SambaConfigureHandler
{
    private const string ShareManagedBlockStart = "# BEGIN EXCPANEL TRANSFER AGENT MANAGED BLOCK";
    private const string ShareManagedBlockEnd = "# END EXCPANEL TRANSFER AGENT MANAGED BLOCK";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IPrivilegedCommandRunner _commandRunner;

    public SambaConfigureHandler(IPrivilegedCommandRunner commandRunner)
    {
        _commandRunner = commandRunner;
    }

    public async Task<PrivilegedHelperResponse> HandleAsync(
        string requestId,
        JsonElement payloadElement,
        CancellationToken cancellationToken)
    {
        SambaConfigurePayload payload;
        try
        {
            payload = payloadElement.Deserialize<SambaConfigurePayload>(JsonOptions)
                ?? throw new JsonException("Payload is null.");
        }
        catch (JsonException)
        {
            return PrivilegedHelperResponse.Failure(
                requestId,
                PrivilegedHelperErrorCodes.InvalidArguments,
                "samba.configure payload is invalid.");
        }

        var completedSteps = new List<string>();
        var validationError = ValidatePayload(payload);
        if (validationError is not null)
        {
            validationError.RequestId = requestId;
            return validationError;
        }

        var storageRoot = payload.StorageRoot.Trim();
        if (!Directory.Exists(storageRoot))
        {
            return PrivilegedHelperResponse.Failure(
                requestId,
                PrivilegedHelperErrorCodes.StorageNotMounted,
                $"Storage root '{storageRoot}' does not exist.",
                completedSteps: completedSteps);
        }

        var mountCheck = await _commandRunner.RunAsync(
            "findmnt",
            ["-n", "-M", storageRoot],
            cancellationToken: cancellationToken);
        if (mountCheck.ExitCode != 0)
        {
            return PrivilegedHelperResponse.Failure(
                requestId,
                PrivilegedHelperErrorCodes.StorageNotMounted,
                $"Storage root '{storageRoot}' is not mounted.",
                completedSteps: completedSteps);
        }

        completedSteps.Add("VerifyStorageRoot");

        var groupResolved = await IsAdGroupResolvedAsync(payload.RequiredAdGroup, cancellationToken);
        if (!groupResolved)
        {
            return PrivilegedHelperResponse.Failure(
                requestId,
                PrivilegedHelperErrorCodes.AdGroupNotResolved,
                $"Required AD group '{payload.RequiredAdGroup}' could not be resolved.",
                completedSteps: completedSteps);
        }

        completedSteps.Add("VerifyRequiredAdGroup");

        var shareName = payload.ShareName.Trim();
        var shareConfig = BuildShareConfig(payload);
        var configFilePath = payload.ConfigFilePath.Trim();
        var mainConfigPath = payload.MainConfigPath.Trim();
        var backupPaths = new List<string>();

        string? configBackup = null;
        string? mainBackup = null;

        try
        {
            if (File.Exists(configFilePath))
            {
                configBackup = CreateBackupPath(configFilePath);
                File.Copy(configFilePath, configBackup, overwrite: false);
                backupPaths.Add(configBackup);
            }

            completedSteps.Add("BackupSambaIncludeFile");

            if (File.Exists(mainConfigPath))
            {
                mainBackup = CreateBackupPath(mainConfigPath);
                File.Copy(mainConfigPath, mainBackup, overwrite: false);
                backupPaths.Add(mainBackup);
            }

            completedSteps.Add("BackupMainSmbConf");

            await WriteFileAtomicallyAsync(configFilePath, shareConfig, cancellationToken);
            completedSteps.Add("WriteSambaIncludeFile");

            var mainContent = File.Exists(mainConfigPath)
                ? await File.ReadAllTextAsync(mainConfigPath, cancellationToken)
                : string.Empty;
            var updatedMain = SambaMainConfigManager.UpsertGlobalIncludeBlock(mainContent, configFilePath);
            await WriteFileAtomicallyAsync(mainConfigPath, updatedMain, cancellationToken);
            completedSteps.Add("UpdateMainSmbConfInclude");

            var aclFailure = await ApplyStorageAclsAsync(requestId, storageRoot, payload.RequiredAdGroup, completedSteps, cancellationToken);
            if (aclFailure is not null)
            {
                await RollbackAsync(configFilePath, mainConfigPath, configBackup, mainBackup, cancellationToken);
                return aclFailure;
            }

            var testparmFailure = await ValidateWithTestparmAsync(
                requestId,
                shareName,
                completedSteps,
                cancellationToken);
            if (testparmFailure is not null)
            {
                await RollbackAsync(configFilePath, mainConfigPath, configBackup, mainBackup, cancellationToken);
                completedSteps.Add("RollbackConfiguration");
                return testparmFailure;
            }

            completedSteps.Add("ValidateWithTestparm");
            var shareConfigured = true;

            var reloadResult = await _commandRunner.RunAsync(
                "systemctl",
                ["reload", "smbd"],
                cancellationToken: cancellationToken);
            if (reloadResult.ExitCode != 0)
            {
                await RollbackAsync(configFilePath, mainConfigPath, configBackup, mainBackup, cancellationToken);
                completedSteps.Add("RollbackConfiguration");
                return FailureFromCommand(
                    requestId,
                    "ReloadSmbd",
                    reloadResult,
                    completedSteps,
                    PrivilegedHelperErrorCodes.SambaReloadFailed);
            }

            completedSteps.Add("ReloadSmbd");

            var shareReachable = await IsShareListedInEffectiveConfigAsync(shareName, cancellationToken);
            completedSteps.Add("VerifyShareListed");

            var data = JsonSerializer.SerializeToElement(new SambaConfigureResultData
            {
                Configured = true,
                ShareConfigured = shareConfigured,
                MainConfigUpdated = true,
                ConfigurationValid = true,
                ShareReachableLocally = shareReachable,
                ConfigFilePath = configFilePath,
                MainConfigPath = mainConfigPath,
                BackupPaths = backupPaths
            }, JsonOptions);

            return PrivilegedHelperResponse.Ok(
                requestId,
                "Samba share configured successfully.",
                completedSteps,
                data);
        }
        catch (IOException ex)
        {
            await RollbackAsync(configFilePath, mainConfigPath, configBackup, mainBackup, cancellationToken);
            return PrivilegedHelperResponse.Failure(
                requestId,
                PrivilegedHelperErrorCodes.SambaValidationFailed,
                "Samba configuration failed while writing files.",
                failedStep: "WriteConfiguration",
                completedSteps: completedSteps,
                stderr: ex.Message);
        }
    }

    private static string BuildShareConfig(SambaConfigurePayload payload)
    {
        var shareName = payload.ShareName.Trim();
        var storageRoot = payload.StorageRoot.Trim();
        var validUsers = SambaValidationHelpers.FormatSambaValidUsers(payload.RequiredAdGroup);
        var guestOk = payload.AllowGuest ? "yes" : "no";

        return $"""
{ShareManagedBlockStart}
[{shareName}]
    path = {storageRoot}
    browseable = no
    guest ok = {guestOk}
    read only = no
    create mask = 0660
    directory mask = 0770
    inherit permissions = yes
    follow symlinks = no
    wide links = no
    valid users = {validUsers}
{ShareManagedBlockEnd}
""";
    }

    private async Task<PrivilegedHelperResponse?> ValidateWithTestparmAsync(
        string requestId,
        string shareName,
        List<string> completedSteps,
        CancellationToken cancellationToken)
    {
        var result = await _commandRunner.RunAsync(
            "testparm",
            ["-s"],
            cancellationToken: cancellationToken);

        if (result.ExitCode != 0)
        {
            return FailureFromCommand(
                requestId,
                "ValidateWithTestparm",
                result,
                completedSteps,
                PrivilegedHelperErrorCodes.SambaValidationFailed);
        }

        if (!SambaTestparmHelpers.ContainsShareSection(result.Stdout, shareName))
        {
            return PrivilegedHelperResponse.Failure(
                requestId,
                PrivilegedHelperErrorCodes.SambaValidationFailed,
                $"Effective Samba configuration does not contain share '[{shareName}]'.",
                failedStep: "ValidateWithTestparm",
                completedSteps: completedSteps,
                stderr: result.Stderr);
        }

        return null;
    }

    private async Task<bool> IsShareListedInEffectiveConfigAsync(string shareName, CancellationToken cancellationToken)
    {
        var result = await _commandRunner.RunAsync(
            "testparm",
            ["-s"],
            cancellationToken: cancellationToken);

        return result.ExitCode == 0 &&
               SambaTestparmHelpers.ContainsShareSection(result.Stdout, shareName);
    }

    private async Task<PrivilegedHelperResponse?> ApplyStorageAclsAsync(
        string requestId,
        string storageRoot,
        string requiredAdGroup,
        List<string> completedSteps,
        CancellationToken cancellationToken)
    {
        var setDefault = await _commandRunner.RunAsync(
            "setfacl",
            ["-R", "-m", $"g:{requiredAdGroup}:rwx", storageRoot],
            cancellationToken: cancellationToken);
        if (setDefault.ExitCode != 0)
        {
            return FailureFromCommand(requestId, "ApplyStorageAcl", setDefault, completedSteps);
        }

        var defaultAcl = await _commandRunner.RunAsync(
            "setfacl",
            ["-R", "-d", "-m", $"g:{requiredAdGroup}:rwx", storageRoot],
            cancellationToken: cancellationToken);
        if (defaultAcl.ExitCode != 0)
        {
            return FailureFromCommand(requestId, "ApplyDefaultStorageAcl", defaultAcl, completedSteps);
        }

        completedSteps.Add("ApplyStorageAcls");
        return null;
    }

    private async Task<bool> IsAdGroupResolvedAsync(string requiredAdGroup, CancellationToken cancellationToken)
    {
        var getent = await _commandRunner.RunAsync(
            "getent",
            ["group", requiredAdGroup.Trim()],
            cancellationToken: cancellationToken);
        if (getent.ExitCode == 0 && !string.IsNullOrWhiteSpace(getent.Stdout))
        {
            return true;
        }

        var wbinfo = await _commandRunner.RunAsync(
            "wbinfo",
            ["--group-info", requiredAdGroup.Trim()],
            cancellationToken: cancellationToken);
        return wbinfo.ExitCode == 0 && !string.IsNullOrWhiteSpace(wbinfo.Stdout);
    }

    private static PrivilegedHelperResponse? ValidatePayload(SambaConfigurePayload payload)
    {
        var errors = new List<string>();
        errors.AddRange(SambaValidationHelpers.ValidateShareName(payload.ShareName));
        errors.AddRange(SambaValidationHelpers.ValidateStorageRoot(payload.StorageRoot));
        errors.AddRange(SambaValidationHelpers.ValidateRequiredAdGroup(payload.RequiredAdGroup));

        if (payload.AllowGuest)
        {
            errors.Add("Guest access must remain disabled.");
        }

        if (string.IsNullOrWhiteSpace(payload.ConfigFilePath) || string.IsNullOrWhiteSpace(payload.MainConfigPath))
        {
            errors.Add("Samba config paths are required.");
        }

        if (errors.Count == 0)
        {
            return null;
        }

        return PrivilegedHelperResponse.Failure(
            string.Empty,
            PrivilegedHelperErrorCodes.InvalidArguments,
            errors[0]);
    }

    private static async Task WriteFileAtomicallyAsync(string targetPath, string content, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = $"{targetPath}.excpanel-temp-{Guid.NewGuid():N}";
        await File.WriteAllTextAsync(tempPath, content, cancellationToken);
        File.Move(tempPath, targetPath, overwrite: true);
    }

    private static async Task RollbackAsync(
        string configFilePath,
        string mainConfigPath,
        string? configBackup,
        string? mainBackup,
        CancellationToken cancellationToken)
    {
        if (configBackup is not null && File.Exists(configBackup))
        {
            await WriteFileAtomicallyAsync(configFilePath, await File.ReadAllTextAsync(configBackup, cancellationToken), cancellationToken);
        }
        else if (File.Exists(configFilePath))
        {
            File.Delete(configFilePath);
        }

        if (mainBackup is not null && File.Exists(mainBackup))
        {
            await WriteFileAtomicallyAsync(mainConfigPath, await File.ReadAllTextAsync(mainBackup, cancellationToken), cancellationToken);
        }
    }

    private static string CreateBackupPath(string originalPath)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        return $"{originalPath}.excpanel-backup-{timestamp}";
    }

    private static PrivilegedHelperResponse FailureFromCommand(
        string requestId,
        string step,
        CommandExecutionResult result,
        IReadOnlyList<string> completedSteps,
        string? errorCode = null) =>
        PrivilegedHelperResponse.Failure(
            requestId,
            errorCode ?? PrivilegedHelperErrorCodes.CommandFailed,
            $"Samba configuration failed at step '{step}'.",
            failedStep: step,
            completedSteps: completedSteps,
            exitCode: result.ExitCode,
            stderr: result.Stderr);
}
