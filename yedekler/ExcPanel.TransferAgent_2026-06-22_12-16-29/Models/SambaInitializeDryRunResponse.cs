namespace ExcPanel.TransferAgent.Models;

public class SambaInitializeDryRunResponse
{
    public bool Supported { get; set; }
    public bool Valid { get; set; }
    public IReadOnlyList<string> PlannedActions { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> ValidationErrors { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
}
