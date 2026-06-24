namespace ExcPanel.TransferAgent.Models;

public class SambaInitializeStepResult
{
    public string Step { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? Message { get; set; }
}
