namespace ExcPanel.TransferAgent.Models;

public class SftpStateDocument
{
    public bool Initialized { get; set; }
    public DateTime? InitializedAtUtc { get; set; }
    public List<SftpUserRecord> Users { get; set; } = [];
}
