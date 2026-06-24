namespace ExcPanel.TransferAgent.Contracts.Validation;

public static class SambaValidationHelpers
{
  private static readonly HashSet<string> AllowedProtocols = new(StringComparer.OrdinalIgnoreCase)
  {
    "SMB2",
    "SMB3",
    "SMB2_02",
    "SMB2_10",
    "SMB3_00",
    "SMB3_02",
    "SMB3_11"
  };

  public static IReadOnlyList<string> ValidateShareName(string? shareName)
  {
    if (string.IsNullOrWhiteSpace(shareName))
    {
      return ["shareName is required."];
    }

    var trimmed = shareName.Trim();
    if (trimmed.Length > 80)
    {
      return ["shareName exceeds maximum length of 80 characters."];
    }

    foreach (var ch in trimmed)
    {
      if (!char.IsLetterOrDigit(ch) && ch is not '_' and not '$' and not '-')
      {
        return [$"shareName contains invalid character '{ch}'."];
      }
    }

    if (trimmed.Contains("..", StringComparison.Ordinal))
    {
      return ["shareName must not contain path traversal sequences."];
    }

    return Array.Empty<string>();
  }

  public static IReadOnlyList<string> ValidateServerName(string? serverName)
  {
    if (string.IsNullOrWhiteSpace(serverName))
    {
      return Array.Empty<string>();
    }

    var trimmed = serverName.Trim();
    if (trimmed.Length > 255)
    {
      return ["serverName exceeds maximum length of 255 characters."];
    }

    foreach (var ch in trimmed)
    {
      if (!char.IsLetterOrDigit(ch) && ch is not '.' and not '-')
      {
        return [$"serverName contains invalid character '{ch}'."];
      }
    }

    return Array.Empty<string>();
  }

  public static IReadOnlyList<string> ValidateStorageRoot(string? storageRoot)
  {
    if (string.IsNullOrWhiteSpace(storageRoot))
    {
      return ["storageRoot is required."];
    }

    var trimmed = storageRoot.Trim();
    if (trimmed.Contains("..", StringComparison.Ordinal))
    {
      return ["storageRoot must not contain path traversal sequences."];
    }

    if (!Path.IsPathRooted(trimmed))
    {
      return ["storageRoot must be an absolute path."];
    }

    return Array.Empty<string>();
  }

  public static IReadOnlyList<string> ValidateRequiredAdGroup(string? requiredAdGroup)
  {
    if (string.IsNullOrWhiteSpace(requiredAdGroup))
    {
      return ["requiredAdGroup is required."];
    }

    var trimmed = requiredAdGroup.Trim();
    if (trimmed.Contains("..", StringComparison.Ordinal) || trimmed.Contains('/', StringComparison.Ordinal))
    {
      return ["requiredAdGroup format is invalid."];
    }

  if (!trimmed.Contains('\\', StringComparison.Ordinal))
    {
      return ["requiredAdGroup must be in DOMAIN\\Group format."];
    }

    var parts = trimmed.Split('\\', 2);
    if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
    {
      return ["requiredAdGroup must be in DOMAIN\\Group format."];
    }

    return Array.Empty<string>();
  }

  public static IReadOnlyList<string> ValidateProtocol(string? protocol, string fieldName)
  {
    if (string.IsNullOrWhiteSpace(protocol))
    {
      return [$"{fieldName} is required."];
    }

    if (!AllowedProtocols.Contains(protocol.Trim()))
    {
      return [$"{fieldName} value '{protocol}' is not supported."];
    }

    return Array.Empty<string>();
  }

  public static IReadOnlyList<string> ValidateStorageRootsMatch(string agentStorageRoot, string sambaStorageRoot)
  {
    try
    {
      var normalizedAgent = Path.GetFullPath(agentStorageRoot.Trim());
      var normalizedSamba = Path.GetFullPath(sambaStorageRoot.Trim());

      if (!string.Equals(normalizedAgent, normalizedSamba, StringComparison.Ordinal))
      {
        return
        [
          $"Samba StorageRoot '{normalizedSamba}' does not match agent StorageRootPath '{normalizedAgent}'."
        ];
      }
    }
    catch
    {
      return ["Storage root paths could not be compared."];
    }

    return Array.Empty<string>();
  }

  public static string NormalizeServerName(string? configuredServerName, string? machineName, string? fqdn)
  {
    if (!string.IsNullOrWhiteSpace(configuredServerName))
    {
      return configuredServerName.Trim().ToUpperInvariant();
    }

    if (!string.IsNullOrWhiteSpace(fqdn))
    {
      var hostPart = fqdn.Split('.', 2)[0];
      if (!string.IsNullOrWhiteSpace(hostPart))
      {
        return hostPart.Trim().ToUpperInvariant();
      }
    }

    return (machineName ?? string.Empty).Trim().ToUpperInvariant();
  }

  public static string BuildUncRoot(string serverName, string shareName) =>
    $@"\\{serverName}\{shareName}";

  public static string BuildUncDirectory(string uncRoot, string relativePath) =>
    $@"{uncRoot}\{relativePath.Replace('/', '\\')}";

  public static string BuildUncFilePath(string uncDirectory, string fileName) =>
    $@"{uncDirectory}\{fileName}";

  public static string GetSuggestedFileName(string jobTypeSegment) =>
    string.Equals(jobTypeSegment, "imports", StringComparison.OrdinalIgnoreCase)
      ? "mailbox.pst"
      : "mailbox.pst";

  public static string FormatSambaValidUsers(string requiredAdGroup)
  {
    var trimmed = requiredAdGroup.Trim();
    if (trimmed.StartsWith('@'))
    {
      trimmed = trimmed[1..];
    }

    return $@"+""{trimmed.Replace(@"\", @"\\")}""";
  }
}
