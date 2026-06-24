namespace ExcPanel.TransferAgent.Models;

public class AgentHealthResponse
{
    public string AgentName { get; set; } = string.Empty;
    public string AgentId { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public PlatformInfo Platform { get; set; } = new();
    public string StorageRootPath { get; set; } = string.Empty;
    public bool SftpEnabled { get; set; }
    public bool SambaEnabled { get; set; }
    public DateTime UtcTime { get; set; }
}
