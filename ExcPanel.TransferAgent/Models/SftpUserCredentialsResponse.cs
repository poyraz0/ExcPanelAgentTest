namespace ExcPanel.TransferAgent.Models;

public class SftpUserCredentialsResponse
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public string RemotePath { get; set; } = "/files";
    public string AccessType { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
}
