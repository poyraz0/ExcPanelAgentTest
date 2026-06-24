namespace ExcPanel.TransferAgent.Contracts;

public static class PrivilegedHelperErrorCodes
{
    public const string NotLinux = "NOT_LINUX";
    public const string NotRoot = "NOT_ROOT";
    public const string InvalidArguments = "INVALID_ARGUMENTS";
    public const string UnknownAction = "UNKNOWN_ACTION";
    public const string InvalidDiskPath = "INVALID_DISK_PATH";
    public const string PartitionPathNotAllowed = "PARTITION_PATH_NOT_ALLOWED";
    public const string SystemDisk = "SYSTEM_DISK";
    public const string DiskMounted = "DISK_MOUNTED";
    public const string DiskHasPartitions = "DISK_HAS_PARTITIONS";
    public const string DiskIdentityMismatch = "DISK_IDENTITY_MISMATCH";
    public const string InvalidMountPath = "INVALID_MOUNT_PATH";
    public const string InvalidFileSystem = "INVALID_FILE_SYSTEM";
    public const string InvalidOwner = "INVALID_OWNER";
    public const string AlreadyConfigured = "ALREADY_CONFIGURED";
    public const string CommandNotAllowed = "COMMAND_NOT_ALLOWED";
    public const string CommandFailed = "COMMAND_FAILED";
    public const string IdempotencyConflict = "IDEMPOTENCY_CONFLICT";
    public const string InternalError = "INTERNAL_ERROR";

    public const string SftpNotInitialized = "SFTP_NOT_INITIALIZED";
    public const string SftpNotImplemented = "SFTP_NOT_IMPLEMENTED";
    public const string InvalidUsername = "INVALID_USERNAME";
    public const string InvalidJobPath = "INVALID_JOB_PATH";
    public const string JobMarkerMissing = "JOB_MARKER_MISSING";
    public const string JobMarkerMismatch = "JOB_MARKER_MISMATCH";
    public const string UserExists = "USER_EXISTS";
    public const string UserNotFound = "USER_NOT_FOUND";
    public const string ActiveSessionConflict = "ACTIVE_SESSION_CONFLICT";
    public const string BindMountConflict = "BIND_MOUNT_CONFLICT";
    public const string SshdValidationFailed = "SSHD_VALIDATION_FAILED";
    public const string SshdReloadFailed = "SSHD_RELOAD_FAILED";

    public const string SambaNotImplemented = "SAMBA_NOT_IMPLEMENTED";
    public const string SambaNotInitialized = "SAMBA_NOT_INITIALIZED";
    public const string DomainNotJoined = "DOMAIN_NOT_JOINED";
    public const string AdGroupNotResolved = "AD_GROUP_NOT_RESOLVED";
    public const string StorageNotMounted = "STORAGE_NOT_MOUNTED";
    public const string SambaValidationFailed = "SAMBA_VALIDATION_FAILED";
    public const string SambaReloadFailed = "SAMBA_RELOAD_FAILED";
}
