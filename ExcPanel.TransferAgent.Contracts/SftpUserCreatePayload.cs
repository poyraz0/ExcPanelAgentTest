namespace ExcPanel.TransferAgent.Contracts;

public class SftpUserCreatePayload
{
    public string Username { get; set; } = string.Empty;
    public string PlaintextPassword { get; set; } = string.Empty;
    public SftpJobType JobType { get; set; }
    public string JobId { get; set; } = string.Empty;
    public string StorageRootPath { get; set; } = string.Empty;
    public string JobPath { get; set; } = string.Empty;
    public string ChrootBasePath { get; set; } = "/var/lib/excpanel-sftp/chroots";
    public string ExportGroupName { get; set; } = "excpanel-sftp-export";
    public string ImportGroupName { get; set; } = "excpanel-sftp-import";
    public DateTime ExpiresAtUtc { get; set; }
    public string MarkerFileName { get; set; } = ".excpanel-job.json";
}
