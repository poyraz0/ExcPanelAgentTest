namespace ExcPanel.TransferAgent.Models.Setup;

public class SetupState
{
    public string SetupId { get; set; } = string.Empty;
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string CurrentStep { get; set; } = string.Empty;
    public SetupStatus Status { get; set; } = SetupStatus.NotStarted;
    public List<SetupStepState> Steps { get; set; } = [];
    public string? LastError { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
