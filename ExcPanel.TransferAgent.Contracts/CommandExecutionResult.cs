namespace ExcPanel.TransferAgent.Contracts;

public class CommandExecutionResult
{
    public string Executable { get; set; } = string.Empty;
    public IReadOnlyList<string> Arguments { get; set; } = Array.Empty<string>();
    public int ExitCode { get; set; }
    public string Stdout { get; set; } = string.Empty;
    public string Stderr { get; set; } = string.Empty;
    public bool TimedOut { get; set; }
}
