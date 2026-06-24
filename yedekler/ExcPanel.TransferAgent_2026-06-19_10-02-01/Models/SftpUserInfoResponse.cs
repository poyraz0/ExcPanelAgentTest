namespace ExcPanel.TransferAgent.Models;

public class SftpUserInfoResponse
{
    public string Username { get; set; } = string.Empty;
    public string JobId { get; set; } = string.Empty;
    public JobDirectoryType JobType { get; set; }
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public string RemotePath { get; set; } = "/files";
    public string AccessType { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public SftpUserStatus Status { get; set; }
    public SftpBindMountStatus BindMountStatus { get; set; }
    public string? BindMountMessage { get; set; }
}
