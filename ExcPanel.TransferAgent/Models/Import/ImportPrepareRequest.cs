namespace ExcPanel.TransferAgent.Models.Import;

public class ImportPrepareRequest
{
    public string? JobId { get; set; }
    public string Mailbox { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
}
