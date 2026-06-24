namespace ExcPanel.TransferAgent.Models.Setup;

public class SetupApplyResponse
{
    public SetupStatus Status { get; set; }
    public string CurrentStep { get; set; } = string.Empty;
    public List<SetupStepState> Steps { get; set; } = [];
    public SetupSummary? Summary { get; set; }
    public string? LastError { get; set; }
}
