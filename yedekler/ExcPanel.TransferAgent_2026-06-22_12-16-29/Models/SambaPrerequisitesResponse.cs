namespace ExcPanel.TransferAgent.Models;

public class SambaPrerequisitesResponse
{
    public bool Supported { get; set; }
    public bool Ready { get; set; }
    public IReadOnlyList<SambaPrerequisiteItem> Items { get; set; } = Array.Empty<SambaPrerequisiteItem>();
}
