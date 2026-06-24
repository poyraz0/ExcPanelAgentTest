namespace ExcPanel.TransferAgent.Models.Setup;

public class SetupSummary
{
    public bool StorageConfigured { get; set; }
    public bool DomainJoined { get; set; }
    public bool SambaConfigured { get; set; }
    public bool SftpInitialized { get; set; }
    public string? StorageRootPath { get; set; }
    public string? UncRoot { get; set; }
    public List<string> Warnings { get; set; } = [];
}
