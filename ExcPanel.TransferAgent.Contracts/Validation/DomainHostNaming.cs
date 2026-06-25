using System.Text.RegularExpressions;

namespace ExcPanel.TransferAgent.Contracts.Validation;

public static partial class DomainHostNaming
{
    public static bool TryValidateComputerName(string? computerName, out string? error)
    {
        if (string.IsNullOrWhiteSpace(computerName))
        {
            error = "computerName is required.";
            return false;
        }

        var trimmed = computerName.Trim();
        if (trimmed.Length > 15)
        {
            error = "computerName must be 15 characters or fewer.";
            return false;
        }

        if (!ComputerNamePattern().IsMatch(trimmed))
        {
            error = "computerName must contain only letters, digits, and hyphens, and cannot start or end with a hyphen.";
            return false;
        }

        error = null;
        return true;
    }

    public static bool TryBuildFqdn(string? computerName, string? dnsDomain, out string fqdn, out string? error)
    {
        fqdn = string.Empty;

        if (!TryValidateComputerName(computerName, out error))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(dnsDomain))
        {
            error = "dnsDomain is required to build the server FQDN.";
            return false;
        }

        fqdn = $"{computerName!.Trim().ToLowerInvariant()}.{dnsDomain.Trim().ToLowerInvariant()}";
        error = null;
        return true;
    }

    public static string ResolveUncHost(string? uncHost, string? computerName, string? dnsDomain)
    {
        if (!string.IsNullOrWhiteSpace(uncHost))
        {
            return uncHost.Trim();
        }

        if (TryBuildFqdn(computerName, dnsDomain, out var fqdn, out _))
        {
            return fqdn;
        }

        return string.Empty;
    }

    [GeneratedRegex(@"^[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,13}[a-zA-Z0-9])?$", RegexOptions.CultureInvariant)]
    private static partial Regex ComputerNamePattern();
}
