using ExcPanel.TransferAgent.Contracts.Validation;
using ExcPanel.TransferAgent.Models;
using ExcPanel.TransferAgent.Services.Storage;

namespace ExcPanel.TransferAgent.Tests.Validation;

public class StorageConfigureValidationTests
{
    [Fact]
    public void ValidateDiskSelection_RejectsSystemDisk()
    {
        var disk = new StorageDiskInfo
        {
            Path = "/dev/sda",
            Name = "sda",
            IsSystemDisk = true,
            Recommended = false
        };

        var errors = StorageConfigureValidation.ValidateDiskSelection(disk, "/dev/sda", StorageValidationHelpers.AllowedMountPath);
        Assert.Contains(errors, error => error.Contains("system disk", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateDiskSelection_RejectsMountedDisk()
    {
        var disk = new StorageDiskInfo
        {
            Path = "/dev/sdz",
            Name = "sdz",
            IsMounted = true,
            Recommended = false
        };

        var errors = StorageConfigureValidation.ValidateDiskSelection(disk, "/dev/sdz", StorageValidationHelpers.AllowedMountPath);
        Assert.Contains(errors, error => error.Contains("mounted", StringComparison.OrdinalIgnoreCase));
    }
}
