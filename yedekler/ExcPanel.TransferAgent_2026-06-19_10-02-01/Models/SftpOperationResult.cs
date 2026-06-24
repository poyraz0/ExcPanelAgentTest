namespace ExcPanel.TransferAgent.Models;

public class SftpOperationResult<T>
{
    public SftpOperationStatus Status { get; init; }
    public string? Message { get; init; }
    public T? Data { get; init; }

    public static SftpOperationResult<T> Ok(T data, string? message = null) => new()
    {
        Status = SftpOperationStatus.Success,
        Data = data,
        Message = message
    };

    public static SftpOperationResult<T> Fail(SftpOperationStatus status, string message) => new()
    {
        Status = status,
        Message = message
    };
}
