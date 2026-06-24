namespace ExcPanel.TransferAgent.Contracts;

public class SambaConfigureResultData
{
    public bool Configured { get; set; }
    public bool ShareConfigured { get; set; }
    public bool MainConfigUpdated { get; set; }
    public bool ConfigurationValid { get; set; }
    public bool ShareReachableLocally { get; set; }
    public string ConfigFilePath { get; set; } = string.Empty;
    public string MainConfigPath { get; set; } = string.Empty;
    public IReadOnlyList<string> BackupPaths { get; set; } = Array.Empty<string>();
}
