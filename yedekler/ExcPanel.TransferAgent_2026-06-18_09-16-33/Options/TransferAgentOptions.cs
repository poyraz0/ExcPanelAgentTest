namespace ExcPanel.TransferAgent.Options;

public class TransferAgentOptions
{
    public const string SectionName = "TransferAgent";

    public string AgentName { get; set; } = "ExcPanel Transfer Agent";
    public string AgentId { get; set; } = string.Empty;
    public string PublicHostName { get; set; } = string.Empty;
    public string StorageRootPath { get; set; } = "/var/excpanel/storage";
    public string SambaShareName { get; set; } = "excpanel-pst";
    public int SftpPort { get; set; } = 22;
    public bool EnableSftp { get; set; }
    public bool EnableSamba { get; set; }
    public string StorageOwnerUser { get; set; } = "agentdev";
    public string StorageOwnerGroup { get; set; } = "agentdev";
    public string StorageDirectoryMode { get; set; } = "750";
    public bool UseSudoForPrivilegedCommands { get; set; }
}
