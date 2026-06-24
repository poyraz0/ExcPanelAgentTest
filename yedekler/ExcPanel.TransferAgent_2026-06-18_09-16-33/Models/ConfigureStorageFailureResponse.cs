namespace ExcPanel.TransferAgent.Models;

public class ConfigureStorageFailureResponse
{
    public string? FailedStep { get; set; }
    public IReadOnlyList<string> ValidationErrors { get; set; } = Array.Empty<string>();
    public CommandResult? FailedCommand { get; set; }
}
