namespace ExcPanel.TransferAgent.Models.Setup;

public class SetupPlanResponse
{
    public List<SetupPlanStep> Steps { get; set; } = [];
    public List<SetupDestructiveAction> DestructiveActions { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
    public List<string> RequiredConfirmationCodes { get; set; } = [];
    public bool CanApply { get; set; }
    public List<string> ValidationErrors { get; set; } = [];
}
