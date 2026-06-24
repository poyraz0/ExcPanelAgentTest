namespace ExcPanel.TransferAgent.Models.Setup;

public class SetupSambaWriteTestResponse
{
    public bool Success { get; set; }
    public bool LocalWriteSucceeded { get; set; }
    public bool ShareConfigured { get; set; }
    public bool AclPresent { get; set; }
    public string? UncDirectory { get; set; }
    public string? TestFilePath { get; set; }
    public List<string> Warnings { get; set; } = [];
    public List<string> Errors { get; set; } = [];
}
