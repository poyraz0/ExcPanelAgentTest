namespace ExcPanel.TransferAgent.Models;

public class JobDirectoryOperationResult<T>
{
    public JobDirectoryOperationStatus Status { get; init; }
    public string? Message { get; init; }
    public T? Data { get; init; }

    public static JobDirectoryOperationResult<T> Ok(T data, string? message = null) => new()
    {
        Status = JobDirectoryOperationStatus.Success,
        Data = data,
        Message = message
    };

    public static JobDirectoryOperationResult<T> Fail(
        JobDirectoryOperationStatus status,
        string message) => new()
    {
        Status = status,
        Message = message
    };
}
