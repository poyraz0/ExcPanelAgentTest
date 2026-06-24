using System.Diagnostics;
using System.Text;
using System.Text.Json;
using ExcPanel.TransferAgent.Contracts;
using ExcPanel.TransferAgent.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExcPanel.TransferAgent.Services.PrivilegedHelper;

public class LinuxPrivilegedHelperClient : IPrivilegedHelperClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly TransferAgentOptions _options;
    private readonly ILogger<LinuxPrivilegedHelperClient> _logger;

    public LinuxPrivilegedHelperClient(
        IOptions<TransferAgentOptions> options,
        ILogger<LinuxPrivilegedHelperClient> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<PrivilegedHelperResponse> InvokeAsync(
        PrivilegedHelperRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_options.PrivilegedHelperPath))
        {
            return PrivilegedHelperResponse.Failure(
                request.RequestId,
                "HELPER_NOT_FOUND",
                "Privileged helper is not installed or authorized.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "sudo",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add("-n");
        startInfo.ArgumentList.Add(_options.PrivilegedHelperPath);

        using var process = new Process { StartInfo = startInfo };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to start privileged helper.");
            return PrivilegedHelperResponse.Failure(
                request.RequestId,
                "HELPER_START_FAILED",
                "Privileged helper is not installed or authorized.");
        }

        var requestJson = JsonSerializer.Serialize(request, JsonOptions);
        await process.StandardInput.WriteAsync(requestJson);
        await process.StandardInput.FlushAsync();
        process.StandardInput.Close();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(_options.PrivilegedHelperTimeoutSeconds));

        var stdoutBuilder = new StringBuilder();
        var stderrBuilder = new StringBuilder();

        var stdoutTask = ReadStreamAsync(process.StandardOutput, stdoutBuilder, timeoutCts.Token);
        var stderrTask = ReadStreamAsync(process.StandardError, stderrBuilder, timeoutCts.Token);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
            await Task.WhenAll(stdoutTask, stderrTask);
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

            return PrivilegedHelperResponse.Failure(
                request.RequestId,
                "HELPER_TIMEOUT",
                "Privileged helper timed out.");
        }

        var stdout = stdoutBuilder.ToString().Trim();
        var stderr = stderrBuilder.ToString().Trim();

        if (process.ExitCode != 0 && string.IsNullOrWhiteSpace(stdout))
        {
            if (stderr.Contains("password", StringComparison.OrdinalIgnoreCase) ||
                stderr.Contains("not allowed", StringComparison.OrdinalIgnoreCase) ||
                stderr.Contains("no new privileges", StringComparison.OrdinalIgnoreCase))
            {
                return PrivilegedHelperResponse.Failure(
                    request.RequestId,
                    "HELPER_NOT_AUTHORIZED",
                    "Privileged helper is not installed or authorized.");
            }

            return PrivilegedHelperResponse.Failure(
                request.RequestId,
                "HELPER_EXECUTION_FAILED",
                "Privileged helper execution failed.",
                stderr: stderr);
        }

        try
        {
            var response = JsonSerializer.Deserialize<PrivilegedHelperResponse>(stdout, JsonOptions);
            if (response is null)
            {
                return PrivilegedHelperResponse.Failure(
                    request.RequestId,
                    "HELPER_INVALID_RESPONSE",
                    "Privileged helper returned an invalid response.");
            }

            return response;
        }
        catch (JsonException)
        {
            return PrivilegedHelperResponse.Failure(
                request.RequestId,
                "HELPER_INVALID_RESPONSE",
                "Privileged helper returned an invalid response.",
                stderr: stderr);
        }
    }

    private static async Task ReadStreamAsync(StreamReader reader, StringBuilder builder, CancellationToken cancellationToken)
    {
        var buffer = new char[4096];
        while (!reader.EndOfStream)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = await reader.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            builder.Append(buffer, 0, read);
        }
    }
}
