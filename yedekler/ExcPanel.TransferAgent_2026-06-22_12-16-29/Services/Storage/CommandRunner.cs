using System.Diagnostics;
using ExcPanel.TransferAgent.Contracts;
using ExcPanel.TransferAgent.Models;
using ExcPanel.TransferAgent.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExcPanel.TransferAgent.Services.Storage;

public class CommandRunner : ICommandRunner
{
    private readonly TransferAgentOptions _options;
    private readonly ILogger<CommandRunner> _logger;

    public CommandRunner(IOptions<TransferAgentOptions> options, ILogger<CommandRunner> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<CommandResult> RunAsync(
        string command,
        string args,
        int timeoutSeconds = 120,
        bool privileged = false,
        CancellationToken cancellationToken = default)
    {
        var (executable, arguments) = ResolveCommand(command, args, privileged);
        _logger.LogInformation("Executing command: {Command} {Args}", executable, arguments);

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

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
                Command = privileged && _options.UseSudoForPrivilegedCommands ? $"sudo {command}" : command,
                Args = args,
                TimeoutSeconds = timeoutSeconds,
                ExitCode = process.ExitCode,
                Stdout = stdout.Trim(),
                Stderr = stderr.Trim()
            };

            if (result.ExitCode != 0)
            {
                _logger.LogWarning(
                    "Command failed: {Command} {Args} | ExitCode={ExitCode} | Stderr={Stderr}",
                    result.Command, result.Args, result.ExitCode, result.Stderr);
            }
            else
            {
                _logger.LogInformation("Command succeeded: {Command} {Args}", result.Command, result.Args);
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
                // Best effort kill on timeout.
            }

            var timeoutResult = new CommandResult
            {
                Command = command,
                Args = args,
                TimeoutSeconds = timeoutSeconds,
                ExitCode = -1,
                Stderr = $"Command timed out after {timeoutSeconds} seconds."
            };

            _logger.LogError("Command timed out: {Command} {Args}", command, args);
            return timeoutResult;
        }
    }

    private (string Executable, string Arguments) ResolveCommand(string command, string args, bool privileged)
    {
        if (privileged && _options.UseSudoForPrivilegedCommands && !PrivilegeHelper.IsRunningAsRoot())
        {
            return ("sudo", $"-n {command} {args}");
        }

        return (command, args);
    }
}
