using System.Text;
using System.Text.Json;
using ExcPanel.TransferAgent.Contracts;
using ExcPanel.TransferAgent.Contracts.Validation;
using ExcPanel.TransferAgent.PrivilegedHelper.Commands;

namespace ExcPanel.TransferAgent.PrivilegedHelper.Handlers;

public class DomainJoinHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IPrivilegedCommandRunner _commandRunner;

    public DomainJoinHandler(IPrivilegedCommandRunner commandRunner)
    {
        _commandRunner = commandRunner;
    }

    public async Task<PrivilegedHelperResponse> HandleAsync(
        string requestId,
        JsonElement payloadElement,
        CancellationToken cancellationToken)
    {
        DomainJoinPayload payload;
        try
        {
            payload = payloadElement.Deserialize<DomainJoinPayload>(JsonOptions)
                ?? throw new JsonException("Payload is null.");
        }
        catch (JsonException)
        {
            return PrivilegedHelperResponse.Failure(
                requestId,
                PrivilegedHelperErrorCodes.InvalidArguments,
                "domain.join payload is invalid.");
        }

        var completedSteps = new List<string>();
        var validationError = ValidatePayload(payload);
        if (validationError is not null)
        {
            validationError.RequestId = requestId;
            return validationError;
        }

        var testJoin = await _commandRunner.RunAsync("net", ["ads", "testjoin"], cancellationToken: cancellationToken);
        if (testJoin.ExitCode == 0)
        {
            var domain = await ResolveJoinedDomainAsync(cancellationToken);
            var data = JsonSerializer.SerializeToElement(new DomainJoinResultData
            {
                Joined = true,
                Skipped = true,
                JoinedDomain = domain,
                TestJoinSucceeded = true
            }, JsonOptions);

            return PrivilegedHelperResponse.Ok(
                requestId,
                "Server is already joined to the domain.",
                ["DomainJoinSkipped"],
                data);
        }

        var krb5Path = payload.Krb5ConfPath.Trim();
        var mainConfigPath = payload.MainConfigPath.Trim();
        string? krb5Backup = null;
        string? smbBackup = null;

        try
        {
            if (File.Exists(krb5Path))
            {
                krb5Backup = CreateBackupPath(krb5Path);
                File.Copy(krb5Path, krb5Backup, overwrite: false);
            }

            completedSteps.Add("BackupKrb5Conf");

            var krb5Content = BuildKrb5Conf(payload);
            await WriteFileAtomicallyAsync(krb5Path, krb5Content, cancellationToken);
            completedSteps.Add("WriteKrb5Conf");

            if (File.Exists(mainConfigPath))
            {
                smbBackup = CreateBackupPath(mainConfigPath);
                File.Copy(mainConfigPath, smbBackup, overwrite: false);
            }

            completedSteps.Add("BackupSmbConf");

            var smbContent = File.Exists(mainConfigPath)
                ? await File.ReadAllTextAsync(mainConfigPath, cancellationToken)
                : string.Empty;
            var updatedSmb = UpsertGlobalDomainMemberSettings(smbContent, payload);
            await WriteFileAtomicallyAsync(mainConfigPath, updatedSmb, cancellationToken);
            completedSteps.Add("UpdateSmbGlobalDomainSettings");

            var joinFailure = await RunNetAdsJoinAsync(requestId, payload, completedSteps, cancellationToken);
            if (joinFailure is not null)
            {
                await RollbackAsync(krb5Path, mainConfigPath, krb5Backup, smbBackup, cancellationToken);
                completedSteps.Add("RollbackConfiguration");
                return joinFailure;
            }

            completedSteps.Add("NetAdsJoin");

            var restartSmbd = await _commandRunner.RunAsync("systemctl", ["restart", "smbd"], cancellationToken: cancellationToken);
            if (restartSmbd.ExitCode != 0)
            {
                return FailureFromCommand(requestId, "RestartSmbd", restartSmbd, completedSteps);
            }

            completedSteps.Add("RestartSmbd");

            var restartWinbind = await _commandRunner.RunAsync("systemctl", ["restart", "winbind"], cancellationToken: cancellationToken);
            if (restartWinbind.ExitCode != 0)
            {
                return FailureFromCommand(requestId, "RestartWinbind", restartWinbind, completedSteps);
            }

            completedSteps.Add("RestartWinbind");

            var verifyJoin = await _commandRunner.RunAsync("net", ["ads", "testjoin"], cancellationToken: cancellationToken);
            if (verifyJoin.ExitCode != 0)
            {
                await RollbackAsync(krb5Path, mainConfigPath, krb5Backup, smbBackup, cancellationToken);
                completedSteps.Add("RollbackConfiguration");
                return FailureFromCommand(requestId, "VerifyTestJoin", verifyJoin, completedSteps, PrivilegedHelperErrorCodes.DomainNotJoined);
            }

            completedSteps.Add("VerifyTestJoin");

            var dcPing = await _commandRunner.RunAsync("wbinfo", ["--ping-dc"], cancellationToken: cancellationToken);
            completedSteps.Add("PingDc");

            var groupResolved = await IsAdGroupResolvedAsync(payload.RequiredAdGroup, cancellationToken);
            if (!groupResolved)
            {
                return PrivilegedHelperResponse.Failure(
                    requestId,
                    PrivilegedHelperErrorCodes.AdGroupNotResolved,
                    $"Required AD group '{payload.RequiredAdGroup}' could not be resolved after join.",
                    completedSteps: completedSteps);
            }

            completedSteps.Add("VerifyRequiredAdGroup");

            var joinedDomain = await ResolveJoinedDomainAsync(cancellationToken);
            var resultData = JsonSerializer.SerializeToElement(new DomainJoinResultData
            {
                Joined = true,
                Skipped = false,
                JoinedDomain = joinedDomain ?? payload.DnsDomain,
                TestJoinSucceeded = true,
                DcPingSucceeded = dcPing.ExitCode == 0,
                RequiredAdGroupResolved = true,
                BackupPaths = new[] { krb5Backup, smbBackup }.Where(p => p is not null).Cast<string>().ToList()
            }, JsonOptions);

            return PrivilegedHelperResponse.Ok(
                requestId,
                "Domain join completed successfully.",
                completedSteps,
                resultData);
        }
        catch (IOException ex)
        {
            await RollbackAsync(krb5Path, mainConfigPath, krb5Backup, smbBackup, cancellationToken);
            return PrivilegedHelperResponse.Failure(
                requestId,
                PrivilegedHelperErrorCodes.CommandFailed,
                "Domain join failed while writing configuration files.",
                failedStep: "WriteConfiguration",
                completedSteps: completedSteps,
                stderr: ex.Message);
        }
    }

    private async Task<PrivilegedHelperResponse?> RunNetAdsJoinAsync(
        string requestId,
        DomainJoinPayload payload,
        List<string> completedSteps,
        CancellationToken cancellationToken)
    {
        if (!_commandRunner.ResolvedExecutables.TryGetValue("net", out var netExecutable))
        {
            return PrivilegedHelperResponse.Failure(
                requestId,
                PrivilegedHelperErrorCodes.CommandFailed,
                "net executable is not available.",
                failedStep: "NetAdsJoin",
                completedSteps: completedSteps);
        }

        var args = new List<string> { "ads", "join", "-U", payload.Username.Trim() };
        if (!string.IsNullOrWhiteSpace(payload.ComputerOu))
        {
            args.Add($"ou={payload.ComputerOu.Trim()}");
        }

        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = netExecutable,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = System.Diagnostics.Process.Start(startInfo);
        if (process is null)
        {
            return PrivilegedHelperResponse.Failure(
                requestId,
                PrivilegedHelperErrorCodes.CommandFailed,
                "Failed to start net ads join.",
                failedStep: "NetAdsJoin",
                completedSteps: completedSteps);
        }

        await process.StandardInput.WriteAsync(payload.Password);
        await process.StandardInput.FlushAsync();
        process.StandardInput.Close();

        var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            return PrivilegedHelperResponse.Failure(
                requestId,
                PrivilegedHelperErrorCodes.CommandFailed,
                "net ads join failed.",
                failedStep: "NetAdsJoin",
                completedSteps: completedSteps,
                exitCode: process.ExitCode,
                stderr: stderr);
        }

        return null;
    }

    private static string BuildKrb5Conf(DomainJoinPayload payload)
    {
        var realm = payload.Realm.Trim().ToUpperInvariant();
        var kdc = string.IsNullOrWhiteSpace(payload.DomainControllerIp)
            ? payload.DomainController.Trim()
            : payload.DomainControllerIp.Trim();
        var dnsDomain = payload.DnsDomain.Trim().ToLowerInvariant();
        var dc = payload.DomainController.Trim();

        return "[libdefaults]\n" +
               $"    default_realm = {realm}\n" +
               "    dns_lookup_realm = false\n" +
               "    dns_lookup_kdc = true\n\n" +
               "[realms]\n" +
               $"    {realm} = {{\n" +
               $"        kdc = {kdc}\n" +
               $"        admin_server = {dc}\n" +
               "    }\n\n" +
               "[domain_realm]\n" +
               $"    .{dnsDomain} = {realm}\n" +
               $"    {dnsDomain} = {realm}\n";
    }

    private static string UpsertGlobalDomainMemberSettings(string mainConfigContent, DomainJoinPayload payload)
    {
        const string blockStart = "# BEGIN EXCPANEL DOMAIN MEMBER";
        const string blockEnd = "# END EXCPANEL DOMAIN MEMBER";

        var lines = mainConfigContent.Replace("\r\n", "\n").Split('\n').ToList();
        lines = RemoveBlock(lines, blockStart, blockEnd);

        var globalStart = lines.FindIndex(l => l.Trim().Equals("[global]", StringComparison.OrdinalIgnoreCase));
        var block = new List<string>
        {
            blockStart,
            $"    workgroup = {payload.Workgroup.Trim()}",
            "    security = ads",
            $"    realm = {payload.Realm.Trim().ToUpperInvariant()}",
            "    idmap config * : backend = tdb",
            "    idmap config * : range = 10000-99999",
            "    winbind use default domain = yes",
            "    winbind enum users = no",
            "    winbind enum groups = no",
            blockEnd
        };

        if (globalStart < 0)
        {
            var result = new List<string> { "[global]" };
            result.AddRange(block);
            result.Add(string.Empty);
            result.AddRange(lines);
            return string.Join(Environment.NewLine, result).TrimEnd() + Environment.NewLine;
        }

        var globalEnd = lines.Count;
        for (var i = globalStart + 1; i < lines.Count; i++)
        {
            if (lines[i].TrimStart().StartsWith('['))
            {
                globalEnd = i;
                break;
            }
        }

        lines.Insert(globalEnd, string.Empty);
        for (var i = block.Count - 1; i >= 0; i--)
        {
            lines.Insert(globalEnd, block[i]);
        }

        return string.Join(Environment.NewLine, lines).TrimEnd() + Environment.NewLine;
    }

    private static List<string> RemoveBlock(List<string> lines, string start, string end)
    {
        var result = new List<string>();
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].Trim().Equals(start, StringComparison.Ordinal))
            {
                i++;
                while (i < lines.Count && !lines[i].Trim().Equals(end, StringComparison.Ordinal))
                {
                    i++;
                }

                continue;
            }

            result.Add(lines[i]);
        }

        return result;
    }

    private async Task<string?> ResolveJoinedDomainAsync(CancellationToken cancellationToken)
    {
        var result = await _commandRunner.RunAsync("realm", ["list"], cancellationToken: cancellationToken);
        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.Stdout))
        {
            return null;
        }

        return result.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
    }

    private async Task<bool> IsAdGroupResolvedAsync(string requiredAdGroup, CancellationToken cancellationToken)
    {
        var getent = await _commandRunner.RunAsync("getent", ["group", requiredAdGroup.Trim()], cancellationToken: cancellationToken);
        if (getent.ExitCode == 0 && !string.IsNullOrWhiteSpace(getent.Stdout))
        {
            return true;
        }

        var wbinfo = await _commandRunner.RunAsync("wbinfo", ["--group-info", requiredAdGroup.Trim()], cancellationToken: cancellationToken);
        return wbinfo.ExitCode == 0 && !string.IsNullOrWhiteSpace(wbinfo.Stdout);
    }

    private static PrivilegedHelperResponse? ValidatePayload(DomainJoinPayload payload)
    {
        if (string.IsNullOrWhiteSpace(payload.DnsDomain) ||
            string.IsNullOrWhiteSpace(payload.Realm) ||
            string.IsNullOrWhiteSpace(payload.Workgroup) ||
            string.IsNullOrWhiteSpace(payload.Username) ||
            string.IsNullOrWhiteSpace(payload.Password))
        {
            return PrivilegedHelperResponse.Failure(
                string.Empty,
                PrivilegedHelperErrorCodes.InvalidArguments,
                "Domain join payload is incomplete.");
        }

        return null;
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

    private static string CreateBackupPath(string originalPath) =>
        $"{originalPath}.excpanel-backup-{DateTime.UtcNow:yyyyMMddHHmmss}";

    private static async Task RollbackAsync(
        string krb5Path,
        string smbPath,
        string? krb5Backup,
        string? smbBackup,
        CancellationToken cancellationToken)
    {
        if (krb5Backup is not null && File.Exists(krb5Backup))
        {
            await File.WriteAllTextAsync(krb5Path, await File.ReadAllTextAsync(krb5Backup, cancellationToken), cancellationToken);
        }

        if (smbBackup is not null && File.Exists(smbBackup))
        {
            await File.WriteAllTextAsync(smbPath, await File.ReadAllTextAsync(smbBackup, cancellationToken), cancellationToken);
        }
    }

    private static PrivilegedHelperResponse FailureFromCommand(
        string requestId,
        string failedStep,
        CommandExecutionResult result,
        List<string> completedSteps,
        string? errorCode = null) =>
        PrivilegedHelperResponse.Failure(
            requestId,
            errorCode ?? PrivilegedHelperErrorCodes.CommandFailed,
            $"Domain join failed at step '{failedStep}'.",
            failedStep: failedStep,
            completedSteps: completedSteps,
            exitCode: result.ExitCode,
            stderr: result.Stderr);
}
