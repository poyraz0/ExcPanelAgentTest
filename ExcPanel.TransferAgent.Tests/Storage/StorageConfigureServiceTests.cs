using System.Text.Json;
using ExcPanel.TransferAgent.Contracts;
using ExcPanel.TransferAgent.Contracts.Validation;
using ExcPanel.TransferAgent.Models;
using ExcPanel.TransferAgent.Options;
using ExcPanel.TransferAgent.Services.Storage;
using ExcPanel.TransferAgent.Tests.Fakes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Moq;

namespace ExcPanel.TransferAgent.Tests.Storage;

public class StorageConfigureServiceTests
{
    [Fact]
    public async Task DryRunAsync_PreservesMountPathAlias()
    {
        var service = CreateService();
        var response = await service.DryRunAsync(new StorageConfigureDryRunRequest
        {
            DiskPath = "/dev/sdb",
            MountPath = StorageValidationHelpers.AllowedMountPath
        });

        Assert.Equal(StorageValidationHelpers.AllowedMountPath, response.TargetMountPath);
    }

    [Fact]
    public async Task ConfigureAsync_MapsHelperFailure()
    {
        var helper = new FakePrivilegedHelperClient
        {
            Handler = request => PrivilegedHelperResponse.Failure(
                request.RequestId,
                PrivilegedHelperErrorCodes.CommandFailed,
                "Storage configuration failed at step 'FormatPartition'.",
                failedStep: "FormatPartition",
                exitCode: 1,
                stderr: "mkfs failed")
        };

        var service = CreateService(helper);
        var dryRun = await service.DryRunAsync(new StorageConfigureDryRunRequest
        {
            DiskPath = "/dev/sdb",
            MountPath = StorageValidationHelpers.AllowedMountPath
        });

        var result = await service.ConfigureAsync(new ConfigureStorageRequest
        {
            DiskPath = "/dev/sdb",
            MountPath = StorageValidationHelpers.AllowedMountPath,
            FileSystem = "ext4",
            ConfirmationCode = dryRun.ConfirmationCode!
        });

        Assert.Equal(ConfigureStorageStatus.CommandFailed, result.Status);
        Assert.Equal("FormatPartition", result.Failure?.FailedStep);
        Assert.Contains("Storage configuration failed", result.Message);
    }

    [Fact]
    public async Task ConfigureAsync_ReturnsHelperAuthorizationError()
    {
        var helper = new FakePrivilegedHelperClient
        {
            Handler = request => PrivilegedHelperResponse.Failure(
                request.RequestId,
                "HELPER_NOT_AUTHORIZED",
                "Privileged helper is not installed or authorized.")
        };

        var service = CreateService(helper);
        var dryRun = await service.DryRunAsync(new StorageConfigureDryRunRequest
        {
            DiskPath = "/dev/sdb",
            MountPath = StorageValidationHelpers.AllowedMountPath
        });

        var result = await service.ConfigureAsync(new ConfigureStorageRequest
        {
            DiskPath = "/dev/sdb",
            MountPath = StorageValidationHelpers.AllowedMountPath,
            FileSystem = "ext4",
            ConfirmationCode = dryRun.ConfirmationCode!
        });

        Assert.Equal(ConfigureStorageStatus.ValidationFailed, result.Status);
        Assert.Contains("Privileged helper is not installed or authorized", result.Failure?.ValidationErrors[0]);
    }

    [Fact]
    public async Task ConfigureAsync_WritesAppsettingsStorageJsonOnSuccess()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"excpanel-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var helper = new FakePrivilegedHelperClient
            {
                Handler = request =>
                {
                    var data = JsonSerializer.SerializeToElement(new StorageConfigureResultData
                    {
                        DiskPath = "/dev/sdb",
                        PartitionPath = "/dev/sdb1",
                        MountPath = StorageValidationHelpers.AllowedMountPath,
                        FileSystem = "ext4",
                        Uuid = "uuid-1",
                        CreatedDirectories = [Path.Combine(StorageValidationHelpers.AllowedMountPath, "exports")],
                        FstabUpdated = true,
                        Mounted = true
                    }, PrivilegedHelperJson.SerializerOptions);

                    return PrivilegedHelperResponse.Ok(
                        request.RequestId,
                        "Storage configured successfully.",
                        ["CreateGptPartitionTable"],
                        data);
                }
            };

            var environment = new Mock<IWebHostEnvironment>();
            environment.SetupGet(e => e.ContentRootPath).Returns(tempDir);
            environment.SetupGet(e => e.WebRootPath).Returns(tempDir);

            var service = CreateService(helper, environment.Object);
            var dryRun = await service.DryRunAsync(new StorageConfigureDryRunRequest
            {
                DiskPath = "/dev/sdb",
                MountPath = StorageValidationHelpers.AllowedMountPath
            });

            var result = await service.ConfigureAsync(new ConfigureStorageRequest
            {
                DiskPath = "/dev/sdb",
                MountPath = StorageValidationHelpers.AllowedMountPath,
                FileSystem = "ext4",
                ConfirmationCode = dryRun.ConfirmationCode!
            });

            Assert.Equal(ConfigureStorageStatus.Success, result.Status);
            Assert.Equal(StorageValidationHelpers.AllowedMountPath, result.Success!.MountPath);
            Assert.Equal("/dev/sdb", result.Success.DiskPath);
            var settingsPath = Path.Combine(tempDir, "appsettings.Storage.json");
            Assert.True(File.Exists(settingsPath));
            var json = await File.ReadAllTextAsync(settingsPath);
            using var document = JsonDocument.Parse(json);
            Assert.Equal(
                StorageValidationHelpers.AllowedMountPath,
                document.RootElement.GetProperty("Storage").GetProperty("RootPath").GetString());
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    private static StorageConfigureService CreateService(
        IPrivilegedHelperClient? helperClient = null,
        IWebHostEnvironment? environment = null)
    {
        var discovery = new FakeDiskDiscoveryService
        {
            Disks = [TestDiskFactory.CreateRecommendedDisk()]
        };

        environment ??= CreateEnvironment();
        var contentRoot = environment.ContentRootPath;
        var options = Microsoft.Extensions.Options.Options.Create(new TransferAgentOptions
        {
            StorageOwnerUser = "root",
            StorageOwnerGroup = "root",
            StorageDirectoryMode = "750",
            StateDirectory = contentRoot
        });

        var settingsWriter = new StorageSettingsWriter(
            environment,
            Microsoft.Extensions.Options.Options.Create(new TransferAgentOptions { StateDirectory = contentRoot }));

        return new StorageConfigureService(
            options,
            discovery,
            helperClient ?? new FakePrivilegedHelperClient(),
            new ConfirmationTokenService(options),
            settingsWriter);
    }

    private static IWebHostEnvironment CreateEnvironment()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"excpanel-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        var environment = new Mock<IWebHostEnvironment>();
        environment.SetupGet(e => e.ContentRootPath).Returns(tempDir);
        environment.SetupGet(e => e.WebRootFileProvider).Returns(new NullFileProvider());
        return environment.Object;
    }
}
