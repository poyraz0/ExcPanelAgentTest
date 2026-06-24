namespace ExcPanel.TransferAgent.Models.Setup;

public class SetupValidateExportPathRequest
{
    public string JobId { get; set; } = string.Empty;
    public string Mailbox { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
}
