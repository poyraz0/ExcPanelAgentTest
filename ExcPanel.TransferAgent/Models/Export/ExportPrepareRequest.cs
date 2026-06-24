namespace ExcPanel.TransferAgent.Models.Export;

public class ExportPrepareRequest
{
    public string? JobId { get; set; }
    public string Mailbox { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public int? EstimatedMailboxSizeGb { get; set; }
}
