using System.Runtime.InteropServices;
using ExcPanel.TransferAgent.Contracts;
using ExcPanel.TransferAgent.Models;

namespace ExcPanel.TransferAgent.Services.Platform;

public class PlatformInfoService : IPlatformInfoService
{
    public PlatformInfo GetPlatformInfo()
    {
        return new PlatformInfo
        {
            OsFamily = GetOsFamily(),
            OsDescription = RuntimeInformation.OSDescription,
            MachineName = Environment.MachineName,
            ProcessArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
            FrameworkDescription = RuntimeInformation.FrameworkDescription
        };
    }

    private static string GetOsFamily()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return "Linux";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return "Windows";
        }

        return "Unknown";
    }
}
