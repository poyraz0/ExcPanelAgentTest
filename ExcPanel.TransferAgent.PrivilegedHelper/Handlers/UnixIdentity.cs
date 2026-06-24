namespace ExcPanel.TransferAgent.PrivilegedHelper.Handlers;

internal static class UnixIdentity
{
    public static bool Exists(string userName)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            return false;
        }

        if (!File.Exists("/etc/passwd"))
        {
            return false;
        }

        foreach (var line in File.ReadLines("/etc/passwd"))
        {
            var parts = line.Split(':', StringSplitOptions.None);
            if (parts.Length > 0 && string.Equals(parts[0], userName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public static bool ExistsGroup(string groupName)
    {
        if (string.IsNullOrWhiteSpace(groupName))
        {
            return false;
        }

        if (!File.Exists("/etc/group"))
        {
            return false;
        }

        foreach (var line in File.ReadLines("/etc/group"))
        {
            var parts = line.Split(':', StringSplitOptions.None);
            if (parts.Length > 0 && string.Equals(parts[0], groupName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
