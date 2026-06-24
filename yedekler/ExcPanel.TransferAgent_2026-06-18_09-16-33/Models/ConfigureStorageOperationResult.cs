namespace ExcPanel.TransferAgent.Models;

public enum ConfigureStorageStatus
{
    Success,
    ValidationFailed,
    CommandFailed,
    NotSupported
}

public class ConfigureStorageOperationResult
{
    public ConfigureStorageStatus Status { get; set; }
    public ConfigureStorageResponse? Success { get; set; }
    public ConfigureStorageFailureResponse? Failure { get; set; }
    public string? Message { get; set; }
}
