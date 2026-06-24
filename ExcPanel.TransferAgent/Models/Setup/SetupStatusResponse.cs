namespace ExcPanel.TransferAgent.Models.Setup;

public class SetupStatusResponse
{
    public SetupStatus Status { get; set; }
    public string CurrentStep { get; set; } = string.Empty;
    public List<SetupStepState> Steps { get; set; } = [];
    public string? LastError { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
}
