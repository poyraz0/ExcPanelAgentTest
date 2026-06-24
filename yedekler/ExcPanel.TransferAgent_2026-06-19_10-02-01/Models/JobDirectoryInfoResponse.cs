namespace ExcPanel.TransferAgent.Models;

public class JobDirectoryInfoResponse
{
    public string JobId { get; set; } = string.Empty;
    public JobDirectoryType JobType { get; set; }
    public string RelativePath { get; set; } = string.Empty;
    public string PhysicalPath { get; set; } = string.Empty;
    public bool Exists { get; set; }
    public DateTime? CreatedAtUtc { get; set; }
    public int TotalFileCount { get; set; }
    public long TotalFileSizeBytes { get; set; }
}
