namespace ExcPanel.TransferAgent.Contracts;

public static class PrivilegedHelperActions
{
    public const string StorageConfigure = "storage.configure";
    public const string StorageRemount = "storage.remount";

    public const string SftpInitialize = "sftp.initialize";
    public const string SftpStatus = "sftp.status";
    public const string SftpUserCreate = "sftp.user.create";
    public const string SftpUserDisable = "sftp.user.disable";
    public const string SftpUserDelete = "sftp.user.delete";
    public const string SftpUserStatus = "sftp.user.status";

    // Reserved for future actions (not implemented in v1):
    public const string SambaConfigure = "samba.configure";
    public const string TestDomainJoin = "domain.testjoin";
    public const string DomainJoin = "domain.join";
    public const string PermissionsApplyExchangeAcl = "permissions.applyExchangeAcl";
}
