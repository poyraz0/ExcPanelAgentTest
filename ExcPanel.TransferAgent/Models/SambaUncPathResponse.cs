namespace ExcPanel.TransferAgent.Models;

public class SambaUncPathResponse
{
    public string JobType { get; set; } = string.Empty;
    public string JobId { get; set; } = string.Empty;
    public string PhysicalDirectory { get; set; } = string.Empty;
    public string UncDirectory { get; set; } = string.Empty;
    public string SuggestedFileName { get; set; } = string.Empty;
    public string UncFilePath { get; set; } = string.Empty;
}
