namespace ExcPanel.TransferAgent.Models.Setup;

public class SetupSambaConfig
{
    public string ShareName { get; set; } = "PSTTransfer$";
    public string UncHost { get; set; } = string.Empty;
    public string RequiredAdGroup { get; set; } = string.Empty;
}
