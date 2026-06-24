namespace ExcPanel.TransferAgent.Models.Setup;

public class SetupPrerequisitesResponse
{
    public bool Ready { get; set; }
    public List<SetupPrerequisiteItem> Checks { get; set; } = [];
}
