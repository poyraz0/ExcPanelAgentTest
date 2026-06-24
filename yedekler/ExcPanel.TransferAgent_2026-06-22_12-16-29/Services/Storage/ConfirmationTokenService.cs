using System.Collections.Concurrent;
using ExcPanel.TransferAgent.Contracts;
using ExcPanel.TransferAgent.Options;
using Microsoft.Extensions.Options;

namespace ExcPanel.TransferAgent.Services.Storage;

public class ConfirmationTokenService : IConfirmationTokenService
{
    private sealed class TokenEntry
    {
        public required StorageConfirmationIdentity Identity { get; init; }
        public required DateTimeOffset ExpiresAt { get; init; }
        public bool Used { get; set; }
    }

    private readonly ConcurrentDictionary<string, TokenEntry> _tokens = new(StringComparer.Ordinal);
    private readonly TimeSpan _lifetime;

    public ConfirmationTokenService(IOptions<TransferAgentOptions> options)
    {
        _lifetime = TimeSpan.FromMinutes(options.Value.ConfirmationTokenLifetimeMinutes);
    }

    public string IssueToken(StorageConfirmationIdentity identity)
    {
        PurgeExpiredTokens();

        var token = Guid.NewGuid().ToString("D");
        _tokens[token] = new TokenEntry
        {
            Identity = identity,
            ExpiresAt = DateTimeOffset.UtcNow.Add(_lifetime)
        };

        return token;
    }

    public bool TryConsumeToken(string token, StorageConfirmationIdentity identity, out string? errorMessage)
    {
        PurgeExpiredTokens();
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(token))
        {
            errorMessage = "confirmationCode is required.";
            return false;
        }

        if (!_tokens.TryGetValue(token.Trim(), out var entry))
        {
            errorMessage = "confirmationCode is invalid or has expired.";
            return false;
        }

        if (entry.Used)
        {
            errorMessage = "confirmationCode has already been used.";
            return false;
        }

        if (entry.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            _tokens.TryRemove(token.Trim(), out _);
            errorMessage = "confirmationCode has expired.";
            return false;
        }

        if (!IdentitiesMatch(entry.Identity, identity))
        {
            errorMessage = "confirmationCode does not match the requested disk configuration.";
            return false;
        }

        entry.Used = true;
        return true;
    }

    private void PurgeExpiredTokens()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var pair in _tokens)
        {
            if (pair.Value.ExpiresAt <= now)
            {
                _tokens.TryRemove(pair.Key, out _);
            }
        }
    }

    private static bool IdentitiesMatch(StorageConfirmationIdentity expected, StorageConfirmationIdentity actual) =>
        string.Equals(expected.DiskPath, actual.DiskPath, StringComparison.Ordinal) &&
        string.Equals(expected.DiskSerialOrWwn, actual.DiskSerialOrWwn, StringComparison.OrdinalIgnoreCase) &&
        expected.SizeBytes == actual.SizeBytes &&
        string.Equals(expected.MountPath, actual.MountPath, StringComparison.Ordinal);
}
