namespace ExcPanel.TransferAgent.Models.Setup;

public class SetupApplyRequest : SetupPlanRequest
{
    public SetupConfirmations? Confirmations { get; set; }
    public SetupDomainCredentials? DomainCredentials { get; set; }
}
