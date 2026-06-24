namespace ExcPanel.TransferAgent.Models;

public class SftpDisableUserResponse
{
    public string Username { get; set; } = string.Empty;
    public SftpUserStatus Status { get; set; }
    public bool PasswordLocked { get; set; }
}
