namespace ExcPanel.TransferAgent.Models.Setup;

public class SetupStepState
{
    public string StepName { get; set; } = string.Empty;
    public SetupStepStatus Status { get; set; } = SetupStepStatus.Pending;
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string? Message { get; set; }
    public List<string> ValidationErrors { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
    public Dictionary<string, object?>? Data { get; set; }
}
