namespace ExcPanel.TransferAgent.Models.Domain;

public class DomainPrecheckItem
{
    public string Name { get; set; } = string.Empty;
    public bool Passed { get; set; }
    public string? Message { get; set; }
    public string? Remediation { get; set; }
}
