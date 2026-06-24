namespace ExcPanel.TransferAgent.Models;

public class SambaStatusResponse
{
    public bool Enabled { get; set; }
    public bool Supported { get; set; }
    public string Platform { get; set; } = string.Empty;
    public bool SambaInstalled { get; set; }
    public bool SmbdRunning { get; set; }
    public bool WinbindInstalled { get; set; }
    public bool WinbindRunning { get; set; }
    public bool DomainJoined { get; set; }
    public string? JoinedDomain { get; set; }
    public bool ShareConfigured { get; set; }
    public bool ShareReachableLocally { get; set; }
    public string ShareName { get; set; } = string.Empty;
    public string StorageRoot { get; set; } = string.Empty;
    public string ServerName { get; set; } = string.Empty;
    public string UncRoot { get; set; } = string.Empty;
    public string RequiredAdGroup { get; set; } = string.Empty;
    public bool RequiredAdGroupResolved { get; set; }
    public bool ConfigurationValid { get; set; }
    public IReadOnlyList<string> ValidationErrors { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
}
