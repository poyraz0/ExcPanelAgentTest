namespace ExcPanel.TransferAgent.Models;

public enum SftpOperationStatus
{
    Success,
    BadRequest,
    NotFound,
    Conflict,
    Forbidden,
    NotImplemented,
    NotSupported,
    ServiceUnavailable,
    CommandFailed
}
