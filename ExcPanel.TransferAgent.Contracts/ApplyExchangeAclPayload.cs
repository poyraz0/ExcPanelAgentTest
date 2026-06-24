namespace ExcPanel.TransferAgent.Contracts;

public class ApplyExchangeAclPayload
{
    public string JobDirectoryPath { get; set; } = string.Empty;
    public string StorageRootPath { get; set; } = string.Empty;
    public string RequiredAdGroup { get; set; } = string.Empty;
}
