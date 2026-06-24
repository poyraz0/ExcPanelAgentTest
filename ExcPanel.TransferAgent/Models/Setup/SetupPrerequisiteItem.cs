namespace ExcPanel.TransferAgent.Models.Setup;

public class SetupPrerequisiteItem
{
    public string Name { get; set; } = string.Empty;
    public SetupPrerequisiteStatus Status { get; set; }
    public string? Message { get; set; }
    public string? Remediation { get; set; }
}
