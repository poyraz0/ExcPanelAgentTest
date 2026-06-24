namespace ExcPanel.TransferAgent.Models;

public class SftpDeleteUserResponse
{
    public string Username { get; set; } = string.Empty;
    public bool Deleted { get; set; }
    public bool JobPathPreserved { get; set; }
    public IReadOnlyList<string> AuditSteps { get; set; } = Array.Empty<string>();
}
