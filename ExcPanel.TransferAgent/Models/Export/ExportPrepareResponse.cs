namespace ExcPanel.TransferAgent.Models.Export;

public class ExportPrepareResponse
{
    public string JobId { get; set; } = string.Empty;
    public string Mailbox { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public string PhysicalDirectory { get; set; } = string.Empty;
    public string UncDirectory { get; set; } = string.Empty;
    public string PstFileName { get; set; } = string.Empty;
    public string ExchangeFilePath { get; set; } = string.Empty;
    public bool ReadyForExchangeExport { get; set; }
    public List<string> Warnings { get; set; } = [];
}
