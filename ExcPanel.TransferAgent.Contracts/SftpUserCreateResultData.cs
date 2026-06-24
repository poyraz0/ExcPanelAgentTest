namespace ExcPanel.TransferAgent.Contracts;

public class SftpUserCreateResultData
{
    public string Username { get; set; } = string.Empty;
    public string ChrootPath { get; set; } = string.Empty;
    public string FilesMountPath { get; set; } = string.Empty;
    public string JobPath { get; set; } = string.Empty;
    public SftpJobType JobType { get; set; }
    public string GroupName { get; set; } = string.Empty;
    public bool UserCreated { get; set; }
    public bool BindMountCreated { get; set; }
    public bool Idempotent { get; set; }
}
