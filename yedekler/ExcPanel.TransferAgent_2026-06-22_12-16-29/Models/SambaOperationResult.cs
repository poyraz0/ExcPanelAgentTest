namespace ExcPanel.TransferAgent.Models;

public class SambaOperationResult<T>
{
    public SambaOperationStatus Status { get; init; }
    public string? Message { get; init; }
    public T? Data { get; init; }

    public static SambaOperationResult<T> Ok(T data, string? message = null) => new()
    {
        Status = SambaOperationStatus.Success,
        Data = data,
        Message = message
    };

    public static SambaOperationResult<T> Fail(SambaOperationStatus status, string message) => new()
    {
        Status = status,
        Message = message
    };
}
