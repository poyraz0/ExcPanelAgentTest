namespace ExcPanel.TransferAgent.Contracts.Validation;

public static class SambaTestparmHelpers
{
    public static bool ContainsShareSection(string testparmOutput, string shareName)
    {
        if (string.IsNullOrWhiteSpace(testparmOutput) || string.IsNullOrWhiteSpace(shareName))
        {
            return false;
        }

        var expected = $"[{shareName.Trim()}]";
        foreach (var line in testparmOutput.Replace("\r\n", "\n").Split('\n'))
        {
            if (line.Trim().Equals(expected, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
