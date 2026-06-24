using ExcPanel.TransferAgent.PrivilegedHelper.Commands;

namespace ExcPanel.TransferAgent.PrivilegedHelper.Handlers;

internal static class SftpBindMountHelper
{
    public static Task<bool> IsBindMountActiveAsync(
        IPrivilegedCommandRunner commandRunner,
        string filesMountPath,
        string expectedJobPath,
        CancellationToken cancellationToken) =>
        Task.FromResult(LinuxBindMountVerifier.IsBindMountActive(filesMountPath, expectedJobPath));

    public static Task<bool> PointsToSameDirectoryAsync(
        IPrivilegedCommandRunner commandRunner,
        string mountPath,
        string expectedJobPath,
        CancellationToken cancellationToken) =>
        IsBindMountActiveAsync(commandRunner, mountPath, expectedJobPath, cancellationToken);

    public static bool PointsToSameDirectory(string mountPath, string expectedJobPath) =>
        LinuxBindMountVerifier.IsBindMountActive(mountPath, expectedJobPath);

    public static Task<bool> IsMountPointAsync(
        IPrivilegedCommandRunner commandRunner,
        string mountPath,
        CancellationToken cancellationToken)
    {
        _ = commandRunner;
        _ = cancellationToken;
        return Task.FromResult(LinuxBindMountVerifier.IsMountPoint(mountPath));
    }
}
