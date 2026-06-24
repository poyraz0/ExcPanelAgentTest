using ExcPanel.TransferAgent.Contracts;
using ExcPanel.TransferAgent.Models;

namespace ExcPanel.TransferAgent.Services.Storage;

public static class PrivilegeHelper
{
    public static async Task<(bool CanElevate, string? ErrorMessage)> CheckPrivilegesAsync(
        ICommandRunner commandRunner,
        bool useSudo,
        CancellationToken cancellationToken = default)
    {
        if (IsRunningAsRoot())
        {
            return (true, null);
        }

        if (!useSudo)
        {
            return (false,
                "Transfer agent is not running as root. Run the service as root or enable UseSudoForPrivilegedCommands in appsettings.");
        }

        var sudoCheck = await commandRunner.RunAsync("sudo", "-n true", privileged: false, cancellationToken: cancellationToken);
        if (sudoCheck.ExitCode == 0)
        {
            return (true, null);
        }

        return (false,
            "Passwordless sudo is required for storage configuration. Configure NOPASSWD sudoers for the agent user or run the service as root.");
    }

    public static bool IsRunningAsRoot()
    {
        if (OperatingSystem.IsLinux())
        {
            try
            {
                return Environment.GetEnvironmentVariable("USER") == "root" ||
                       geteuid() == 0;
            }
            catch
            {
                return false;
            }
        }

        return false;
    }

    [System.Runtime.InteropServices.DllImport("libc", SetLastError = true)]
    private static extern uint geteuid();
}
