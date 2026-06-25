using ExcPanel.TransferAgent.Contracts.Validation;
using ExcPanel.TransferAgent.Options;

namespace ExcPanel.TransferAgent.Services.Samba;

public static class SambaConfigBuilder
{
    private const string ShareManagedBlockStart = "# BEGIN EXCPANEL TRANSFER AGENT MANAGED BLOCK";
    private const string ShareManagedBlockEnd = "# END EXCPANEL TRANSFER AGENT MANAGED BLOCK";

    public static string BuildShareConfig(SambaOptions options)
    {
        var shareName = options.ShareName.Trim();
        var storageRoot = options.StorageRoot.Trim();
        var guestOk = options.AllowGuest ? "yes" : "no";
        var authDirectives = string.Join(
            Environment.NewLine,
            SambaValidationHelpers.BuildShareAuthorizationDirectives()
                .Select(line => $"    {line}"));

        return $"""
{ShareManagedBlockStart}
[{shareName}]
    path = {storageRoot}
    browseable = no
    guest ok = {guestOk}
    read only = no
    create mask = 0660
    directory mask = 0770
    follow symlinks = no
    wide links = no
{authDirectives}
{ShareManagedBlockEnd}
""";
    }

    public static string UpsertGlobalIncludeBlock(string mainConfigContent, string configFilePath) =>
        SambaMainConfigManager.UpsertGlobalIncludeBlock(mainConfigContent, configFilePath);

    public static bool ShareSectionMatches(string content, SambaOptions options)
    {
        if (!content.Contains(ShareManagedBlockStart, StringComparison.Ordinal) &&
            !content.Contains(ShareManagedBlockEnd, StringComparison.Ordinal))
        {
            return false;
        }

        var expected = BuildShareConfig(options);
        return string.Equals(
            NormalizeConfig(content),
            NormalizeConfig(expected),
            StringComparison.Ordinal);
    }

    private static string NormalizeConfig(string content) =>
        string.Join('\n', content.Replace("\r\n", "\n").Split('\n').Select(line => line.TrimEnd()));
}
