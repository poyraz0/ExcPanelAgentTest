using System.Text.Json;

namespace ExcPanel.TransferAgent.Contracts;

public class PrivilegedHelperResponse
{
    public bool Success { get; set; }
    public string RequestId { get; set; } = string.Empty;
    public IReadOnlyList<string> CompletedSteps { get; set; } = Array.Empty<string>();
    public string? FailedStep { get; set; }
    public string? ErrorCode { get; set; }
    public string Message { get; set; } = string.Empty;
    public JsonElement Data { get; set; } = JsonDocument.Parse("{}").RootElement.Clone();
    public int? FailedCommandExitCode { get; set; }
    public string? FailedCommandStderr { get; set; }

    public static PrivilegedHelperResponse Failure(
        string requestId,
        string errorCode,
        string message,
        string? failedStep = null,
        IReadOnlyList<string>? completedSteps = null,
        int? exitCode = null,
        string? stderr = null) =>
        new()
        {
            Success = false,
            RequestId = requestId,
            ErrorCode = errorCode,
            Message = message,
            FailedStep = failedStep,
            CompletedSteps = completedSteps ?? Array.Empty<string>(),
            FailedCommandExitCode = exitCode,
            FailedCommandStderr = stderr
        };

    public static PrivilegedHelperResponse Ok(
        string requestId,
        string message,
        IReadOnlyList<string> completedSteps,
        JsonElement data) =>
        new()
        {
            Success = true,
            RequestId = requestId,
            Message = message,
            CompletedSteps = completedSteps,
            Data = data
        };
}
