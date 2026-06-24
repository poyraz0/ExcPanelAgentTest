namespace ExcPanel.TransferAgent.Contracts;

public class SftpUserDisablePayload
{
    public string Username { get; set; } = string.Empty;
    public string ChrootPath { get; set; } = string.Empty;
}
