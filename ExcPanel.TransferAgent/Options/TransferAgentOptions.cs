namespace ExcPanel.TransferAgent.Options;

public class TransferAgentOptions
{
    public const string SectionName = "TransferAgent";

    public string AgentName { get; set; } = "ExcPanel Transfer Agent";
    public string AgentId { get; set; } = string.Empty;
    public string PublicHostName { get; set; } = string.Empty;
    public string StorageRootPath { get; set; } = "/data/excpanel-transfer";
    public string StateDirectory { get; set; } = string.Empty;
    public string SambaShareName { get; set; } = "excpanel-pst";
    public int SftpPort { get; set; } = 22;
    public bool EnableSftp { get; set; }
    public bool EnableSamba { get; set; }
    public string StorageOwnerUser { get; set; } = "excpanel-agent";
    public string StorageOwnerGroup { get; set; } = "excpanel-agent";
    public string StorageDirectoryMode { get; set; } = "750";
    public bool RequireStorageMount { get; set; } = true;
    public bool UseSudoForPrivilegedCommands { get; set; }
    public string PrivilegedHelperPath { get; set; } = "/opt/excpanel-transfer-agent/ExcPanel.TransferAgent.PrivilegedHelper";
    public int PrivilegedHelperTimeoutSeconds { get; set; } = 300;
    public int ConfirmationTokenLifetimeMinutes { get; set; } = 15;

    public string SftpChrootBasePath { get; set; } = "/var/lib/excpanel-sftp/chroots";
    public string SftpExportGroupName { get; set; } = "excpanel-sftp-export";
    public string SftpImportGroupName { get; set; } = "excpanel-sftp-import";
    public string SftpSshdDropInPath { get; set; } = "/etc/ssh/sshd_config.d/60-excpanel-sftp.conf";
    public int SftpCleanupIntervalSeconds { get; set; } = 300;
    public int SftpDeletionGracePeriodMinutes { get; set; } = 60;
}
