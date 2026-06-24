using ExcPanel.TransferAgent.Contracts.Validation;

namespace ExcPanel.TransferAgent.Tests.Samba;

public class SambaMainConfigManagerTests
{
    private const string IncludePath = "/etc/samba/excpanel-transfer.conf";

    [Fact]
    public void UpsertGlobalIncludeBlock_MovesIncludeFromPrintSectionToGlobal()
    {
        const string mainConfig = """
[global]
   workgroup = EXAMPLE

[printers]
   comment = All Printers

[print$]
   comment = Printer Drivers
   path = /var/spool/samba
   include = /etc/samba/excpanel-transfer.conf # excpanel-transfer-agent include
""";

        var updated = SambaMainConfigManager.UpsertGlobalIncludeBlock(mainConfig, IncludePath);
        var lines = updated.Replace("\r\n", "\n").Split('\n');

        var globalIndex = Array.FindIndex(lines, line => line.Trim().Equals("[global]", StringComparison.Ordinal));
        var printIndex = Array.FindIndex(lines, line => line.Trim().Equals("[print$]", StringComparison.Ordinal));
        var includeIndex = Array.FindIndex(lines, line => line.Trim().Equals($"include = {IncludePath}", StringComparison.Ordinal));

        Assert.True(globalIndex >= 0);
        Assert.True(printIndex > globalIndex);
        Assert.True(includeIndex > globalIndex);
        Assert.True(includeIndex < printIndex);
        Assert.DoesNotContain("excpanel-transfer-agent include", updated);
    }

    [Fact]
    public void UpsertGlobalIncludeBlock_DoesNotUseInlineComment()
    {
        const string mainConfig = """
[global]
   workgroup = EXAMPLE
""";

        var updated = SambaMainConfigManager.UpsertGlobalIncludeBlock(mainConfig, IncludePath);

        Assert.Contains("# BEGIN EXCPANEL TRANSFER AGENT", updated);
        Assert.Contains($"include = {IncludePath}", updated);
        Assert.Contains("# END EXCPANEL TRANSFER AGENT", updated);
        Assert.DoesNotContain("excpanel-transfer-agent include", updated);
        Assert.DoesNotContain("# excpanel", updated);
    }

    [Fact]
    public void UpsertGlobalIncludeBlock_IsIdempotent()
    {
        const string mainConfig = """
[global]
   workgroup = EXAMPLE

[print$]
   path = /var/spool/samba
""";

        var first = SambaMainConfigManager.UpsertGlobalIncludeBlock(mainConfig, IncludePath);
        var second = SambaMainConfigManager.UpsertGlobalIncludeBlock(first, IncludePath);

        Assert.Equal(first, second);
        Assert.Equal(1, CountOccurrences(first, "# BEGIN EXCPANEL TRANSFER AGENT"));
        Assert.Equal(1, CountOccurrences(first, $"include = {IncludePath}"));
    }

    [Fact]
    public void ContainsShareSection_RequiresExactSectionHeader()
    {
        const string output = """
[global]
        workgroup = EXAMPLE

[PSTTransfer$]
        path = /data/excpanel-transfer
""";

        Assert.True(SambaTestparmHelpers.ContainsShareSection(output, "PSTTransfer$"));
        Assert.False(SambaTestparmHelpers.ContainsShareSection(output, "PSTTransfer"));
    }

    private static int CountOccurrences(string content, string value) =>
        content.Split('\n').Count(line => line.Trim().Equals(value, StringComparison.Ordinal));
}
