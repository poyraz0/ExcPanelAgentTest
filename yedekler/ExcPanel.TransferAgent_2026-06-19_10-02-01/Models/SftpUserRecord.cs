namespace ExcPanel.TransferAgent.Models;

public class SftpUserRecord
{
    public string Username { get; set; } = string.Empty;
    public string JobId { get; set; } = string.Empty;
    public JobDirectoryType JobType { get; set; }
    public string ChrootPath { get; set; } = string.Empty;
    public string FilesMountPath { get; set; } = string.Empty;
    public string JobPath { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? DisabledAtUtc { get; set; }
    public SftpUserStatus Status { get; set; }
    public SftpBindMountStatus BindMountStatus { get; set; }
    public string? BindMountMessage { get; set; }
}
