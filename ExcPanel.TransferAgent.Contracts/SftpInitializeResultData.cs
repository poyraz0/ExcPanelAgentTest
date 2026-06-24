namespace ExcPanel.TransferAgent.Contracts;

public class SftpInitializeResultData
{
    public bool Initialized { get; set; }
    public bool ExportGroupCreated { get; set; }
    public bool ImportGroupCreated { get; set; }
    public bool ChrootBaseCreated { get; set; }
    public bool SshdDropInInstalled { get; set; }
    public bool SshdReloaded { get; set; }
    public string ChrootBasePath { get; set; } = string.Empty;
    public string SshdDropInPath { get; set; } = string.Empty;
}
