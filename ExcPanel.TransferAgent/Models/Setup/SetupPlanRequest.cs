namespace ExcPanel.TransferAgent.Models.Setup;

public class SetupPlanRequest
{
    public SetupStorageConfig? Storage { get; set; }
    public SetupDomainConfig? Domain { get; set; }
    public SetupSambaConfig? Samba { get; set; }
    public SetupSftpConfig? Sftp { get; set; }
}
