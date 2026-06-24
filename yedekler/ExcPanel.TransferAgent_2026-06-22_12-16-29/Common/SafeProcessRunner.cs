using System.Diagnostics;
using ExcPanel.TransferAgent.Contracts;
using ExcPanel.TransferAgent.Models;
using Microsoft.Extensions.Logging;

namespace ExcPanel.TransferAgent.Common;

public class SafeProcessRunner : ISafeProcessRunner
{
    private readonly ILogger<SafeProcessRunner> _logger;

    public SafeProcessRunner(ILogger<SafeProcessRunner> logger)
    {
        _logger = logger;
    }

    public async Task<CommandResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        int timeoutSeconds = 120,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Executing command: {Command} with {ArgCount} arguments", fileName, arguments.Count);

        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        try
        {
            var outputTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            var errorTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);
            await process.WaitForExitAsync(timeoutCts.Token);

            var stdout = await outputTask;
            var stderr = await errorTask;

            var result = new CommandResult
            {
                Command = fileName,
                Args = string.Join(' ', arguments),
                TimeoutSeconds = timeoutSeconds,
                ExitCode = process.ExitCode,
                Stdout = stdout.Trim(),
                Stderr = stderr.Trim()
            };

            if (result.ExitCode != 0)
            {
                _logger.LogWarning(
                    "Command failed: {Command} | ExitCode={ExitCode}",
                    fileName,
                    result.ExitCode);
            }

            return result;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // Best effort.
            }

            _logger.LogError("Command timed out: {Command}", fileName);
            return new CommandResult
            {
                Command = fileName,
                Args = string.Join(' ', arguments),
                TimeoutSeconds = timeoutSeconds,
                ExitCode = -1,
                Stderr = $"Command timed out after {timeoutSeconds} seconds."
            };
        }
    }
}
