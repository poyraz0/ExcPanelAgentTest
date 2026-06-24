namespace ExcPanel.TransferAgent.Contracts;

public class SftpInitializePayload
{
    public string ChrootBasePath { get; set; } = "/var/lib/excpanel-sftp/chroots";
    public string ExportGroupName { get; set; } = "excpanel-sftp-export";
    public string ImportGroupName { get; set; } = "excpanel-sftp-import";
    public string SshdDropInPath { get; set; } = "/etc/ssh/sshd_config.d/60-excpanel-sftp.conf";
}
