namespace ExcPanel.TransferAgent.Models;

public class JobDirectoryDeleteResponse
{
    public string JobId { get; set; } = string.Empty;
    public JobDirectoryType JobType { get; set; }
    public string RelativePath { get; set; } = string.Empty;
    public bool Deleted { get; set; }
}
