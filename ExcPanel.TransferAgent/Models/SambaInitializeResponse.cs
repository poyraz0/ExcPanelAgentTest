namespace ExcPanel.TransferAgent.Models;

public class SambaInitializeResponse
{
    public bool Initialized { get; set; }
    public bool ShareConfigured { get; set; }
    public bool ConfigurationValid { get; set; }
    public bool ShareReachableLocally { get; set; }
    public string ShareName { get; set; } = string.Empty;
    public string StorageRoot { get; set; } = string.Empty;
    public string ServerName { get; set; } = string.Empty;
    public string UncRoot { get; set; } = string.Empty;
    public IReadOnlyList<SambaInitializeStepResult> Steps { get; set; } = Array.Empty<SambaInitializeStepResult>();
    public IReadOnlyList<string> ValidationErrors { get; set; } = Array.Empty<string>();
}
