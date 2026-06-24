namespace ExcPanel.TransferAgent.Models.Setup;

public class SetupPlanStep
{
    public string StepName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool WillRun { get; set; }
    public bool WillSkip { get; set; }
    public string? SkipReason { get; set; }
}
