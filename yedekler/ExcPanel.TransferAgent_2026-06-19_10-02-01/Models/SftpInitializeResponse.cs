namespace ExcPanel.TransferAgent.Models;

public class SftpInitializeResponse
{
    public bool Initialized { get; set; }
    public DateTime? InitializedAtUtc { get; set; }
}
