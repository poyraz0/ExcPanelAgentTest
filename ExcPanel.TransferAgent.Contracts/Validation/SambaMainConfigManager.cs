namespace ExcPanel.TransferAgent.Contracts.Validation;

public static class SambaMainConfigManager
{
    public const string ManagedBlockStart = "# BEGIN EXCPANEL TRANSFER AGENT";
    public const string ManagedBlockEnd = "# END EXCPANEL TRANSFER AGENT";

    private const string LegacyManagedBlockStart = "# BEGIN EXCPANEL TRANSFER AGENT MANAGED BLOCK";
    private const string LegacyManagedBlockEnd = "# END EXCPANEL TRANSFER AGENT MANAGED BLOCK";
    private const string LegacyIncludeMarker = "excpanel-transfer-agent include";

    public static string UpsertGlobalIncludeBlock(string mainConfigContent, string includeFilePath)
    {
        var includePath = includeFilePath.Trim();
        var lines = SplitLines(mainConfigContent);
        lines = RemoveManagedBlocks(lines);
        lines = RemoveStaleIncludeLines(lines, includePath);

        var globalStart = FindGlobalSectionStart(lines);
        if (globalStart < 0)
        {
            var preamble = new List<string> { "[global]" };
            preamble.AddRange(BuildManagedBlockLines(includePath));
            if (lines.Count > 0)
            {
                preamble.Add(string.Empty);
            }

            preamble.AddRange(lines);
            return JoinLines(preamble);
        }

        var globalEnd = FindSectionEnd(lines, globalStart);
        if (HasCorrectManagedBlockInGlobal(lines, globalStart, globalEnd, includePath))
        {
            return JoinLines(lines);
        }

        while (globalEnd > globalStart + 1 && string.IsNullOrWhiteSpace(lines[globalEnd - 1]))
        {
            lines.RemoveAt(globalEnd - 1);
            globalEnd--;
        }

        var insertIndex = globalEnd;
        if (insertIndex > globalStart + 1)
        {
            lines.Insert(insertIndex, string.Empty);
            insertIndex++;
        }

        var blockLines = BuildManagedBlockLines(includePath);
        for (var i = blockLines.Count - 1; i >= 0; i--)
        {
            lines.Insert(insertIndex, blockLines[i]);
        }

        return JoinLines(lines);
    }

    public static bool ContainsManagedIncludeBlock(string mainConfigContent, string includeFilePath)
    {
        var lines = SplitLines(mainConfigContent);
        var globalStart = FindGlobalSectionStart(lines);
        if (globalStart < 0)
        {
            return false;
        }

        var globalEnd = FindSectionEnd(lines, globalStart);
        return HasCorrectManagedBlockInGlobal(lines, globalStart, globalEnd, includeFilePath.Trim());
    }

    private static IReadOnlyList<string> BuildManagedBlockLines(string includePath) =>
    [
        ManagedBlockStart,
        $"include = {includePath}",
        ManagedBlockEnd
    ];

    private static bool HasCorrectManagedBlockInGlobal(
        IReadOnlyList<string> lines,
        int globalStart,
        int globalEnd,
        string includePath)
    {
        var blockCount = 0;
        for (var i = globalStart + 1; i < globalEnd; i++)
        {
            if (!lines[i].Trim().Equals(ManagedBlockStart, StringComparison.Ordinal))
            {
                continue;
            }

            blockCount++;
            if (i + 2 >= globalEnd ||
                !lines[i + 1].Trim().Equals($"include = {includePath}", StringComparison.Ordinal) ||
                !lines[i + 2].Trim().Equals(ManagedBlockEnd, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return blockCount == 1;
    }

    private static List<string> RemoveStaleIncludeLines(IReadOnlyList<string> lines, string includePath)
    {
        var result = new List<string>(lines.Count);
        foreach (var line in lines)
        {
            if (IsStaleIncludeLine(line.Trim(), includePath))
            {
                continue;
            }

            result.Add(line);
        }

        return result;
    }

    private static bool IsStaleIncludeLine(string trimmed, string includePath)
    {
        if (trimmed.Contains(LegacyIncludeMarker, StringComparison.Ordinal))
        {
            return true;
        }

        if (!trimmed.StartsWith("include", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var path = ExtractIncludePath(trimmed);
        return path is not null &&
               string.Equals(path, includePath, StringComparison.Ordinal);
    }

    private static string? ExtractIncludePath(string includeLine)
    {
        var equalsIndex = includeLine.IndexOf('=');
        if (equalsIndex < 0)
        {
            return null;
        }

        var value = includeLine[(equalsIndex + 1)..].Trim();
        var hashIndex = value.IndexOf('#');
        if (hashIndex >= 0)
        {
            value = value[..hashIndex].Trim();
        }

        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static List<string> RemoveManagedBlocks(IReadOnlyList<string> lines)
    {
        var result = new List<string>(lines.Count);
        for (var i = 0; i < lines.Count; i++)
        {
            if (!IsManagedBlockStart(lines[i].Trim()))
            {
                result.Add(lines[i]);
                continue;
            }

            i++;
            while (i < lines.Count && !IsManagedBlockEnd(lines[i].Trim()))
            {
                i++;
            }
        }

        return result;
    }

    private static bool IsManagedBlockStart(string trimmed) =>
        trimmed.Equals(ManagedBlockStart, StringComparison.Ordinal) ||
        trimmed.Equals(LegacyManagedBlockStart, StringComparison.Ordinal);

    private static bool IsManagedBlockEnd(string trimmed) =>
        trimmed.Equals(ManagedBlockEnd, StringComparison.Ordinal) ||
        trimmed.Equals(LegacyManagedBlockEnd, StringComparison.Ordinal);

    private static int FindGlobalSectionStart(IReadOnlyList<string> lines)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            if (IsSectionHeader(lines[i]) &&
                string.Equals(UnwrapSectionName(lines[i]), "global", StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private static int FindSectionEnd(IReadOnlyList<string> lines, int sectionStart)
    {
        for (var i = sectionStart + 1; i < lines.Count; i++)
        {
            if (IsSectionHeader(lines[i]))
            {
                return i;
            }
        }

        return lines.Count;
    }

    private static bool IsSectionHeader(string line)
    {
        var trimmed = line.Trim();
        return trimmed.Length >= 2 && trimmed.StartsWith('[') && trimmed.EndsWith(']');
    }

    private static string UnwrapSectionName(string line) =>
        line.Trim().Trim('[', ']').Trim();

    private static List<string> SplitLines(string content) =>
        content.Replace("\r\n", "\n").Split('\n').ToList();

    private static string JoinLines(IReadOnlyList<string> lines) =>
        string.Join(Environment.NewLine, lines).TrimEnd() + Environment.NewLine;
}
