namespace ExcPanel.TransferAgent.Models.Import;

public class ImportPrepareResponse
{
    public string JobId { get; set; } = string.Empty;
    public string Mailbox { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public string PhysicalDirectory { get; set; } = string.Empty;
    public string? UncDirectory { get; set; }
    public string? ExchangeFilePath { get; set; }
    public string? SftpPlaceholder { get; set; }
    public bool ReadyForImport { get; set; }
    public List<string> Warnings { get; set; } = [];
}
