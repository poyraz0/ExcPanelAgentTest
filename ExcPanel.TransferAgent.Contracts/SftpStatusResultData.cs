namespace ExcPanel.TransferAgent.Contracts;

public class SftpStatusResultData
{
    public bool Initialized { get; set; }
    public bool ExportGroupExists { get; set; }
    public bool ImportGroupExists { get; set; }
    public bool ChrootBaseExists { get; set; }
    public bool SshdDropInExists { get; set; }
    public string ChrootBasePath { get; set; } = string.Empty;
    public string SshdDropInPath { get; set; } = string.Empty;
}
