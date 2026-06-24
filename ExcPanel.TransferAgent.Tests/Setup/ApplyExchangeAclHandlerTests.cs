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
}
