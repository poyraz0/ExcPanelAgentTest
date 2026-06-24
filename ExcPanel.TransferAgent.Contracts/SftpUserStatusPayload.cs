namespace ExcPanel.TransferAgent.Contracts;

public class SftpUserStatusPayload
{
    public string Username { get; set; } = string.Empty;
    public string ChrootPath { get; set; } = string.Empty;
    public string FilesMountPath { get; set; } = string.Empty;
    public string JobPath { get; set; } = string.Empty;
    public string ExportGroupName { get; set; } = "excpanel-sftp-export";
    public bool EnsureBindMount { get; set; }
}
