namespace ExcPanel.TransferAgent.Models.Setup;

public class SetupConfirmations
{
    public string? FormatDisk { get; set; }
    public bool JoinDomain { get; set; }
    public bool ConfigureSamba { get; set; }
    public bool InitializeSftp { get; set; }
}
