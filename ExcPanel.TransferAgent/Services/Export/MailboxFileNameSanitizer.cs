using System.Text.RegularExpressions;

namespace ExcPanel.TransferAgent.Services.Export;

public static partial class MailboxFileNameSanitizer
{
    [GeneratedRegex(@"[^a-zA-Z0-9._-]+", RegexOptions.CultureInvariant)]
    private static partial Regex InvalidCharsRegex();

    public static bool TryValidateMailbox(string mailbox, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(mailbox))
        {
            error = "mailbox is required.";
            return false;
        }

        if (!mailbox.Contains('@', StringComparison.Ordinal))
        {
            error = "mailbox must be a valid email address.";
            return false;
        }

        var parts = mailbox.Split('@', 2);
        if (string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
        {
            error = "mailbox must be a valid email address.";
            return false;
        }

        return true;
    }

    public static string SanitizePstFileName(string mailbox, string domain)
    {
        var localPart = mailbox.Split('@')[0].Trim().ToLowerInvariant();
        var domainPart = domain.Trim().ToLowerInvariant();
        var sanitizedLocal = InvalidCharsRegex().Replace(localPart, "_");
        var sanitizedDomain = InvalidCharsRegex().Replace(domainPart, "_");
        return $"{sanitizedLocal}_{sanitizedDomain}.pst";
    }
}
