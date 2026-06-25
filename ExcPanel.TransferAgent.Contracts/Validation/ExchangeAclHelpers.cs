namespace ExcPanel.TransferAgent.Contracts.Validation;

public static class ExchangeAclHelpers
{
    public const string DefaultPermissions = "rwx";

    public static string FormatGroupAclSpec(string adGroup, string permissions = DefaultPermissions) =>
        $"g:{adGroup.Trim()}:{permissions}";

    public static string[] BuildModifyAccessArguments(string path, string adGroup, string permissions = DefaultPermissions) =>
        ["-m", FormatGroupAclSpec(adGroup, permissions), path];

    public static string[] BuildModifyDefaultArguments(string path, string adGroup, string permissions = DefaultPermissions) =>
        ["-d", "-m", FormatGroupAclSpec(adGroup, permissions), path];

    public static string[] BuildRecursiveModifyAccessArguments(string path, string adGroup, string permissions = DefaultPermissions) =>
        ["-R", .. BuildModifyAccessArguments(path, adGroup, permissions)];

    public static string[] BuildRecursiveModifyDefaultArguments(string path, string adGroup, string permissions = DefaultPermissions) =>
        ["-R", .. BuildModifyDefaultArguments(path, adGroup, permissions)];
}
