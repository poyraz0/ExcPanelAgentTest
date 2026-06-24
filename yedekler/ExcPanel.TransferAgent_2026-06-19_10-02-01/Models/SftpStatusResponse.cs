namespace ExcPanel.TransferAgent.Models;

public class SftpStatusResponse
{
    public bool Enabled { get; set; }
    public bool Initialized { get; set; }
    public int Port { get; set; }
    public string Host { get; set; } = string.Empty;
    public int ActiveUserCount { get; set; }
    public int DisabledUserCount { get; set; }
    public int ExpiredUserCount { get; set; }
}
