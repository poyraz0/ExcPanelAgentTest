using System.Text.Json;
using ExcPanel.TransferAgent.Contracts;
using ExcPanel.TransferAgent.Contracts.Validation;
using ExcPanel.TransferAgent.Models;
using ExcPanel.TransferAgent.Options;
using ExcPanel.TransferAgent.PrivilegedHelper;
using ExcPanel.TransferAgent.PrivilegedHelper.Commands;
using ExcPanel.TransferAgent.PrivilegedHelper.Handlers;
using ExcPanel.TransferAgent.Services.Storage;
using ExcPanel.TransferAgent.Tests.Fakes;
using Microsoft.Extensions.Options;

namespace ExcPanel.TransferAgent.Tests.Helper;

public class PrivilegedHelperHostTests
{
    [Fact]
    public async Task RunAsync_RejectsCommandLineArguments()
    {
        var host = PrivilegedHelperTestFactory.CreateHost(isRoot: true);

        using var output = new StringWriter();
        var exitCode = await host.RunAsync(["--help"], new StringReader(string.Empty), output);

        Assert.Equal(1, exitCode);
        Assert.Contains("Command-line arguments are not accepted", output.ToString());
    }

    [Fact]
    public async Task RunAsync_RejectsWhenNotRoot()
    {
        var host = PrivilegedHelperTestFactory.CreateHost(isRoot: false);

        using var output = new StringWriter();
        var exitCode = await host.RunAsync([], new StringReader("{}"), output);

        Assert.Equal(1, exitCode);
        Assert.Contains(PrivilegedHelperErrorCodes.NotRoot, output.ToString());
    }

    [Fact]
    public async Task RunAsync_RejectsUnknownAction()
    {
        var host = PrivilegedHelperTestFactory.CreateHost(isRoot: true);
        var request = JsonSerializer.Serialize(new PrivilegedHelperRequest
        {
            RequestId = "req-1",
            Action = "unknown.action",
            Payload = JsonSerializer.SerializeToElement(new { })
        });

        using var output = new StringWriter();
        var exitCode = await host.RunAsync([], new StringReader(request), output);

        Assert.Equal(1, exitCode);
        Assert.Contains(PrivilegedHelperErrorCodes.UnknownAction, output.ToString());
    }
}

public class StorageConfigureHandlerTests
{
    [Fact]
    public async Task HandleAsync_RejectsSystemDisk()
    {
        var runner = CreateLsblkRunner(isSystemDisk: true);
        var handler = new StorageConfigureHandler(runner, new DiskInspector(runner));
        var response = await handler.HandleAsync(
            "req-1",
            CreatePayloadElement(),
            CancellationToken.None);

        Assert.False(response.Success);
        Assert.Equal(PrivilegedHelperErrorCodes.SystemDisk, response.ErrorCode);
    }

    [Fact]
    public async Task HandleAsync_RejectsDiskMountedAtWrongPath()
    {
        var runner = CreateLsblkRunner(isMounted: true, mountPoint: "/mnt/other");
        var handler = new StorageConfigureHandler(runner, new DiskInspector(runner));
        var response = await handler.HandleAsync(
            "req-1",
            CreatePayloadElement(),
            CancellationToken.None);

        Assert.False(response.Success);
        Assert.Equal(PrivilegedHelperErrorCodes.DiskMounted, response.ErrorCode);
    }

    [Fact]
    public async Task HandleAsync_RejectsPartitionPath()
    {
        var runner = new FakePrivilegedCommandRunner();
        var handler = new StorageConfigureHandler(runner, new DiskInspector(runner));
        var payload = new StorageConfigurePayload
        {
            DiskPath = "/dev/sdb1",
            ExpectedSerial = "SERIAL",
            ExpectedSizeBytes = 100,
            MountPath = StorageValidationHelpers.AllowedMountPath,
            FileSystem = "ext4",
            OwnerUser = "root",
            OwnerGroup = "root",
            DirectoryMode = "0750"
        };

        var response = await handler.HandleAsync(
            "req-1",
            JsonSerializer.SerializeToElement(payload),
            CancellationToken.None);

        Assert.False(response.Success);
        Assert.Equal(PrivilegedHelperErrorCodes.PartitionPathNotAllowed, response.ErrorCode);
    }

    [Fact]
    public async Task HandleAsync_RejectsSerialMismatch()
    {
        var runner = CreateLsblkRunner(serial: "ACTUAL-SERIAL");
        var handler = new StorageConfigureHandler(runner, new DiskInspector(runner));
        var response = await handler.HandleAsync(
            "req-1",
            CreatePayloadElement(expectedSerial: "EXPECTED-SERIAL"),
            CancellationToken.None);

        Assert.False(response.Success);
        Assert.Equal(PrivilegedHelperErrorCodes.DiskIdentityMismatch, response.ErrorCode);
    }

    [Fact]
    public async Task HandleAsync_RejectsSizeMismatch()
    {
        var runner = CreateLsblkRunner(sizeBytes: 200);
        var handler = new StorageConfigureHandler(runner, new DiskInspector(runner));
        var response = await handler.HandleAsync(
            "req-1",
            CreatePayloadElement(expectedSizeBytes: 100),
            CancellationToken.None);

        Assert.False(response.Success);
        Assert.Equal(PrivilegedHelperErrorCodes.DiskIdentityMismatch, response.ErrorCode);
    }

    [Fact]
    public async Task HandleAsync_RejectsInvalidMountPath()
    {
        var runner = CreateLsblkRunner();
        var handler = new StorageConfigureHandler(runner, new DiskInspector(runner));
        var payload = new StorageConfigurePayload
        {
            DiskPath = "/dev/sdb",
            ExpectedSerial = "SERIAL",
            ExpectedSizeBytes = 100,
            MountPath = "/tmp/evil",
            FileSystem = "ext4",
            OwnerUser = "root",
            OwnerGroup = "root",
            DirectoryMode = "0750"
        };

        var response = await handler.HandleAsync(
            "req-1",
            JsonSerializer.SerializeToElement(payload),
            CancellationToken.None);

        Assert.False(response.Success);
        Assert.Equal(PrivilegedHelperErrorCodes.InvalidMountPath, response.ErrorCode);
    }

    private static FakePrivilegedCommandRunner CreateLsblkRunner(
        bool isSystemDisk = false,
        bool isMounted = false,
        bool hasPartitions = false,
        string serial = "SERIAL",
        long sizeBytes = 100,
        string? mountPoint = null)
    {
        var runner = new FakePrivilegedCommandRunner();
        runner.SetHandler("lsblk", _ =>
        {
            var mountPointsJson = isMounted
                ? $"[\"{mountPoint ?? "/mnt/data"}\"]"
                : "null";
            var children = hasPartitions
                ? ",\"children\":[{\"name\":\"sdb1\",\"path\":\"/dev/sdb1\",\"size\":100,\"type\":\"part\",\"mountpoints\":null}]"
                : string.Empty;

            var rootMountPoints = isSystemDisk ? "[\"/\"]" : mountPointsJson;
            var stdout =
                $"{{\"blockdevices\":[{{\"name\":\"sdz\",\"path\":\"/dev/sdz\",\"size\":{sizeBytes},\"type\":\"disk\",\"serial\":\"{serial}\",\"wwn\":null,\"mountpoints\":{rootMountPoints}{children}}}]}}";

            return new CommandExecutionResult { ExitCode = 0, Stdout = stdout };
        });

        return runner;
    }

    private static JsonElement CreatePayloadElement(
        string expectedSerial = "SERIAL",
        long expectedSizeBytes = 100,
        string diskPath = "/dev/sdz") =>
        JsonSerializer.SerializeToElement(new StorageConfigurePayload
        {
            DiskPath = diskPath,
            ExpectedSerial = expectedSerial,
            ExpectedSizeBytes = expectedSizeBytes,
            MountPath = StorageValidationHelpers.AllowedMountPath,
            FileSystem = "ext4",
            OwnerUser = "root",
            OwnerGroup = "root",
            DirectoryMode = "0750"
        });
}

public class LinuxPrivilegedCommandRunnerTests
{
    [Fact]
    public async Task RunAsync_PassesStandardInputToChpasswd()
    {
        var runner = new FakePrivilegedCommandRunner();
        await runner.RunAsync(
            "chpasswd",
            Array.Empty<string>(),
            standardInput: "exp_test:secret\n");

        Assert.Contains(runner.StdinPayloads, entry =>
            entry.Key == "chpasswd"
            && entry.StandardInput == "exp_test:secret\n");
    }

    [Fact]
    public async Task RunAsync_RejectsUnknownExecutable()
    {
        var runner = new FakePrivilegedCommandRunner();
        var result = await runner.RunAsync("bash", ["-c", "id"]);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("allowlist", result.Stderr, StringComparison.OrdinalIgnoreCase);
    }
}
