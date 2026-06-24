using ExcPanel.TransferAgent.Common;
using ExcPanel.TransferAgent.Contracts;
using ExcPanel.TransferAgent.Options;
using Microsoft.Extensions.Options;

namespace ExcPanel.TransferAgent.Providers.Linux;

public class LinuxStorageMountChecker : IStorageMountChecker
{
    private readonly TransferAgentOptions _options;

    public LinuxStorageMountChecker(IOptions<TransferAgentOptions> options)
    {
        _options = options.Value;
    }

    public async Task<bool> IsMountedAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!_options.RequireStorageMount)
        {
            return Directory.Exists(path);
        }

        if (!OperatingSystem.IsLinux())
        {
            return Directory.Exists(path);
        }

        var (exitCode, _, _) = await ProcessRunner.RunAsync(
            "findmnt",
            $"-n -M {path}",
            cancellationToken);

        return exitCode == 0;
    }
}
