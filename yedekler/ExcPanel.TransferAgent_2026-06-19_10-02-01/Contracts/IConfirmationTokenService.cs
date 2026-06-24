namespace ExcPanel.TransferAgent.Contracts;

public interface IConfirmationTokenService
{
    string IssueToken(StorageConfirmationIdentity identity);

    bool TryConsumeToken(string token, StorageConfirmationIdentity identity, out string? errorMessage);
}

public sealed class StorageConfirmationIdentity
{
    public string DiskPath { get; init; } = string.Empty;
    public string DiskSerialOrWwn { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public string MountPath { get; init; } = string.Empty;
}
