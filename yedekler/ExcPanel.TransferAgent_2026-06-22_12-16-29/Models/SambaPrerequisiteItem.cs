namespace ExcPanel.TransferAgent.Models;

public class SambaPrerequisiteItem
{
    public string Name { get; set; } = string.Empty;
    public bool Satisfied { get; set; }
    public string? Message { get; set; }
    public IReadOnlyList<string> RecommendedActions { get; set; } = Array.Empty<string>();
}
