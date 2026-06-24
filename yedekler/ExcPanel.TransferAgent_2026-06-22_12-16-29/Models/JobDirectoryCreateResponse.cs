namespace ExcPanel.TransferAgent.Models;

public class JobDirectoryCreateResponse
{
    public string JobId { get; set; } = string.Empty;
    public JobDirectoryType JobType { get; set; }
    public string RelativePath { get; set; } = string.Empty;
    public string PhysicalPath { get; set; } = string.Empty;
    public string? UncDirectory { get; set; }
    public string? SuggestedFileName { get; set; }
    public string? UncFilePath { get; set; }
    public bool Created { get; set; }
}
