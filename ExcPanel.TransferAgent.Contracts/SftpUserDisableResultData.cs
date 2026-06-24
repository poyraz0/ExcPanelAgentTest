namespace ExcPanel.TransferAgent.Contracts;

public class SftpUserDisableResultData
{
    public string Username { get; set; } = string.Empty;
    public bool PasswordLocked { get; set; }
    public bool AlreadyDisabled { get; set; }
}
