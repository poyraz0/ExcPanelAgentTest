using ExcPanel.TransferAgent.Contracts;
using ExcPanel.TransferAgent.PrivilegedHelper.Commands;

namespace ExcPanel.TransferAgent.PrivilegedHelper.Handlers;

internal static class SshdConfigManager
{
    public static string BuildDropInContent(string exportGroupName, string importGroupName, string chrootBasePath) =>
        $"""
        # Managed by ExcPanel Transfer Agent. Do not edit manually.
        Match Group {exportGroupName}
            ChrootDirectory {chrootBasePath}/%u
            ForceCommand internal-sftp -d /files -R
            PasswordAuthentication yes
            PubkeyAuthentication no
            PermitTTY no
            AllowTcpForwarding no
            X11Forwarding no
            GatewayPorts no
            PermitTunnel no

        Match Group {importGroupName}
            ChrootDirectory {chrootBasePath}/%u
            ForceCommand internal-sftp -d /files
            PasswordAuthentication yes
            PubkeyAuthentication no
            PermitTTY no
            AllowTcpForwarding no
            X11Forwarding no
            GatewayPorts no
            PermitTunnel no
        """;

    public static async Task<(bool Success, string? ErrorMessage)> InstallDropInAsync(
        string dropInPath,
        string content,
        IPrivilegedCommandRunner commandRunner,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(dropInPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return (false, "Invalid sshd drop-in path.");
        }

        Directory.CreateDirectory(directory);

        string? backupPath = null;
        if (File.Exists(dropInPath))
        {
            backupPath = $"{dropInPath}.bak.{DateTime.UtcNow:yyyyMMddHHmmss}";
            File.Copy(dropInPath, backupPath, overwrite: true);
        }

        var tempPath = $"{dropInPath}.{Guid.NewGuid():N}.tmp";
        await File.WriteAllTextAsync(tempPath, content, cancellationToken);

        try
        {
            File.Move(tempPath, dropInPath, overwrite: true);
        }
        catch
        {
            TryDeleteFile(tempPath);
            throw;
        }

        var validateResult = await commandRunner.RunAsync("sshd", ["-t", "-f", "/etc/ssh/sshd_config"], cancellationToken: cancellationToken);
        if (validateResult.ExitCode != 0)
        {
            if (backupPath is not null)
            {
                File.Copy(backupPath, dropInPath, overwrite: true);
            }
            else
            {
                TryDeleteFile(dropInPath);
            }

            await commandRunner.RunAsync("sshd", ["-t", "-f", "/etc/ssh/sshd_config"], cancellationToken: cancellationToken);
            return (false, validateResult.Stderr);
        }

        var reloadResult = await ReloadSshdAsync(commandRunner, cancellationToken);
        if (reloadResult.ExitCode != 0)
        {
            if (backupPath is not null)
            {
                File.Copy(backupPath, dropInPath, overwrite: true);
                await commandRunner.RunAsync("sshd", ["-t", "-f", "/etc/ssh/sshd_config"], cancellationToken: cancellationToken);
                await ReloadSshdAsync(commandRunner, cancellationToken);
            }
            else
            {
                TryDeleteFile(dropInPath);
            }

            return (false, reloadResult.Stderr);
        }

        return (true, null);
    }

    public static async Task<CommandExecutionResult> ReloadSshdAsync(
        IPrivilegedCommandRunner commandRunner,
        CancellationToken cancellationToken)
    {
        var reload = await commandRunner.RunAsync("systemctl", ["reload", "ssh"], cancellationToken: cancellationToken);
        if (reload.ExitCode == 0)
        {
            return reload;
        }

        return await commandRunner.RunAsync("systemctl", ["reload", "sshd"], cancellationToken: cancellationToken);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best effort.
        }
    }
}
