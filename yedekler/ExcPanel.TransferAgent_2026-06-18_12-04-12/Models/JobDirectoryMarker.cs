namespace ExcPanel.TransferAgent.Models;

public class JobDirectoryMarker
{
    public string JobId { get; set; } = string.Empty;
    public JobDirectoryType JobType { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string AgentVersion { get; set; } = string.Empty;
}
