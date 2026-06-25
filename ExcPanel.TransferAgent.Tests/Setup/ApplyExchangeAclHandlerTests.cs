using System.Text.Json;
using ExcPanel.TransferAgent.Contracts;
using ExcPanel.TransferAgent.PrivilegedHelper.Handlers;
using ExcPanel.TransferAgent.Tests.Fakes;

namespace ExcPanel.TransferAgent.Tests.Setup;

public class ApplyExchangeAclHandlerTests
{
    [Fact]
    public async Task HandleAsync_RejectsPathOutsideStorageRoot()
    {
        var runner = new FakePrivilegedCommandRunner();
        var handler = new ApplyExchangeAclHandler(runner);
        var storageRoot = Path.Combine(Path.GetTempPath(), $"excpanel-acl-{Guid.NewGuid():N}");
        Directory.CreateDirectory(storageRoot);

        try
        {
            var payload = JsonSerializer.SerializeToElement(new ApplyExchangeAclPayload
            {
                JobDirectoryPath = "/tmp/outside",
                StorageRootPath = storageRoot,
                RequiredAdGroup = @"DOGRU\Exchange Trusted Subsystem"
            });

            var response = await handler.HandleAsync("req-1", payload, CancellationToken.None);
            Assert.False(response.Success);
            Assert.Equal(PrivilegedHelperErrorCodes.InvalidJobPath, response.ErrorCode);
        }
        finally
        {
            if (Directory.Exists(storageRoot))
            {
                Directory.Delete(storageRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task HandleAsync_AppliesTraverseAclOnAncestorsAndRwxOnJobDirectory()
    {
        var runner = new FakePrivilegedCommandRunner();
        runner.SetHandler("setfacl", args =>
        {
            Assert.NotEmpty(args);
            return new CommandExecutionResult { ExitCode = 0 };
        });

        var handler = new ApplyExchangeAclHandler(runner);
        var storageRoot = Path.Combine(Path.GetTempPath(), $"excpanel-acl-{Guid.NewGuid():N}");
        var exportsRoot = Path.Combine(storageRoot, "exports");
        var jobDir = Path.Combine(exportsRoot, Guid.NewGuid().ToString("D"));
        Directory.CreateDirectory(jobDir);

        try
        {
            var payload = JsonSerializer.SerializeToElement(new ApplyExchangeAclPayload
            {
                JobDirectoryPath = jobDir,
                StorageRootPath = storageRoot,
                RequiredAdGroup = @"DOGRU\Exchange Trusted Subsystem"
            });

            var response = await handler.HandleAsync("req-2", payload, CancellationToken.None);
            Assert.True(response.Success);

            var setfaclCalls = runner.Invocations
                .Where(i => i.Key == "setfacl")
                .Select(i => string.Join(' ', i.Arguments))
                .ToList();

            Assert.Contains(setfaclCalls, c => c.Contains(exportsRoot) && c.Contains(":rwx"));
            Assert.Equal(4, setfaclCalls.Count);
            Assert.Equal(2, setfaclCalls.Count(c => c.Contains(jobDir)));
        }
        finally
        {
            if (Directory.Exists(storageRoot))
            {
                Directory.Delete(storageRoot, recursive: true);
            }
        }
    }
}
