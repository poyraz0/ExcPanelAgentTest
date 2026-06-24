namespace ExcPanel.TransferAgent.Models;

public class JobDirectoryCreateResponse
{
    public string JobId { get; set; } = string.Empty;
    public JobDirectoryType JobType { get; set; }
    public string RelativePath { get; set; } = string.Empty;
    public string PhysicalPath { get; set; } = string.Empty;
    public bool Created { get; set; }
}
