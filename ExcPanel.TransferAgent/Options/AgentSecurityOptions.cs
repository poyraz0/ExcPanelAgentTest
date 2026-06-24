namespace ExcPanel.TransferAgent.Options;

public class AgentSecurityOptions
{
    public const string SectionName = "AgentSecurity";

    public string Mode { get; set; } = "Standalone";
    public bool RequireRegistration { get; set; }
    public bool RequireAgentAuth { get; set; }
}
