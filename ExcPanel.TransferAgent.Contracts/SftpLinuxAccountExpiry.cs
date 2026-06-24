namespace ExcPanel.TransferAgent.Contracts;

public static class SftpLinuxAccountExpiry
{
    /// <summary>
    /// Linux account expiry (-e) disables the account at 00:00 on the given calendar date.
    /// Return the following UTC day so the account stays valid through expiresAtUtc.
    /// </summary>
    public static string ComputeExpiryDate(DateTime expiresAtUtc) =>
        expiresAtUtc.ToUniversalTime().Date.AddDays(1).ToString("yyyy-MM-dd");
}
