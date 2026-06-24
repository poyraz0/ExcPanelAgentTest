namespace ExcPanel.TransferAgent.Contracts;

public class SftpUserDeleteResultData
{
    public string Username { get; set; } = string.Empty;
    public bool UserDeleted { get; set; }
    public bool BindMountRemoved { get; set; }
    public bool ChrootRemoved { get; set; }
    public bool JobPathPreserved { get; set; } = true;
    public IReadOnlyList<string> AuditSteps { get; set; } = Array.Empty<string>();
}
