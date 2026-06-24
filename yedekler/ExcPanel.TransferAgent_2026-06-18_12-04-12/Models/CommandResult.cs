namespace ExcPanel.TransferAgent.Models;

public class CommandResult
{
    public string Command { get; set; } = string.Empty;
    public string Args { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; }
    public int ExitCode { get; set; }
    public string Stdout { get; set; } = string.Empty;
    public string Stderr { get; set; } = string.Empty;
}
