namespace ExcPanel.TransferAgent.Contracts;

public class SftpUserStatusResultData
{
    public string Username { get; set; } = string.Empty;
    public bool LinuxUserExists { get; set; }
    public bool PasswordLocked { get; set; }
    public bool BindMountActive { get; set; }
    public bool ChrootExists { get; set; }
    public string? ObservedJobPath { get; set; }
    public bool BindMountRestored { get; set; }
}
