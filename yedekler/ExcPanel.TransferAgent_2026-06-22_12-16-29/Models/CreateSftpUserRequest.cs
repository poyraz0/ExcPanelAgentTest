namespace ExcPanel.TransferAgent.Models;

public class CreateSftpUserRequest
{
    public string JobId { get; set; } = string.Empty;
    public string JobType { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
}
