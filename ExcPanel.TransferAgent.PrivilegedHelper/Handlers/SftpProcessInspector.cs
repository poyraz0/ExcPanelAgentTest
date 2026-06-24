namespace ExcPanel.TransferAgent.PrivilegedHelper.Handlers;

internal static class SftpProcessInspector
{
    public static bool HasActiveSession(string username)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return false;
        }

        if (!Directory.Exists("/proc"))
        {
            return false;
        }

        foreach (var procDir in Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(procDir), out _))
            {
                continue;
            }

            try
            {
                var statusPath = Path.Combine(procDir, "status");
                if (!File.Exists(statusPath))
                {
                    continue;
                }

                string? processUser = null;
                foreach (var line in File.ReadLines(statusPath))
                {
                    if (line.StartsWith("Uid:", StringComparison.Ordinal))
                    {
                        var uidText = line.Split(':', 2)[1].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                        if (int.TryParse(uidText, out var uid))
                        {
                            processUser = ResolveUserName(uid);
                        }

                        break;
                    }
                }

                if (!string.Equals(processUser, username, StringComparison.Ordinal))
                {
                    continue;
                }

                var cmdlinePath = Path.Combine(procDir, "cmdline");
                if (!File.Exists(cmdlinePath))
                {
                    continue;
                }

                var cmdline = File.ReadAllText(cmdlinePath).Replace('\0', ' ').Trim();
                if (cmdline.Contains("sshd", StringComparison.Ordinal)
                    || cmdline.Contains("sftp-server", StringComparison.Ordinal)
                    || cmdline.Contains("internal-sftp", StringComparison.Ordinal))
                {
                    return true;
                }
            }
            catch
            {
                // Ignore unreadable process entries.
            }
        }

        return false;
    }

    private static string? ResolveUserName(int uid)
    {
        if (!File.Exists("/etc/passwd"))
        {
            return null;
        }

        foreach (var line in File.ReadLines("/etc/passwd"))
        {
            var parts = line.Split(':', StringSplitOptions.None);
            if (parts.Length > 2
                && int.TryParse(parts[2], out var entryUid)
                && entryUid == uid)
            {
                return parts[0];
            }
        }

        return null;
    }
}
