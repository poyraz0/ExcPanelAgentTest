using ExcPanel.TransferAgent.Contracts.Validation;

namespace ExcPanel.TransferAgent.Tests.Validation;

public class StorageValidationTests
{
    [Fact]
    public void ValidateMountPath_RejectsInvalidPath()
    {
        var errors = StorageValidationHelpers.ValidateMountPath("/tmp/excpanel-storage");
        Assert.Contains(errors, error => error.Contains("must be exactly"));
    }

    [Fact]
    public void ValidateMountPath_RejectsForbiddenRootPaths()
    {
        var errors = StorageValidationHelpers.ValidateMountPath("/");
        Assert.Contains(errors, error => error.Contains("not allowed") || error.Contains("must be exactly"));
    }

    [Fact]
    public void ValidateMountPath_RejectsPathTraversal()
    {
        var errors = StorageValidationHelpers.ValidateMountPath("/data/../etc/passwd");
        Assert.Contains(errors, error => error.Contains("must be exactly"));
    }

    [Fact]
    public void IsPartitionPath_RejectsPartitionDevices()
    {
        Assert.True(StorageValidationHelpers.IsPartitionPath("/dev/sdb1"));
        Assert.True(StorageValidationHelpers.IsPartitionPath("/dev/nvme0n1p1"));
    }

    [Fact]
    public void IsValidWholeDiskPath_AcceptsSupportedDiskPaths()
    {
        Assert.True(StorageValidationHelpers.IsValidWholeDiskPath("/dev/sdb"));
        Assert.True(StorageValidationHelpers.IsValidWholeDiskPath("/dev/vda"));
        Assert.True(StorageValidationHelpers.IsValidWholeDiskPath("/dev/nvme0n1"));
    }
}
