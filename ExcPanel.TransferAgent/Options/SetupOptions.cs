namespace ExcPanel.TransferAgent.Options;

public class SetupOptions
{
    public const string SectionName = "Setup";

    public string Mode { get; set; } = "Standalone";
    public string StorageRootPath { get; set; } = "/data/excpanel-transfer";
    public string DefaultMountPath { get; set; } = "/data/excpanel-transfer";
    public string DefaultShareName { get; set; } = "PSTTransfer$";
    public string DefaultSftpChrootRoot { get; set; } = "/var/lib/excpanel-sftp/chroots";
    public bool RequireDomainJoinForSamba { get; set; } = true;
    public bool RequireSambaForExchangeExport { get; set; } = true;
    public bool RequireSftpForUserDownload { get; set; } = true;
    public bool AllowHttpInDevelopment { get; set; } = true;
}
