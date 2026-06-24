using ExcPanel.TransferAgent.Contracts;
using ExcPanel.TransferAgent.Models.Setup;
using ExcPanel.TransferAgent.Options;
using ExcPanel.TransferAgent.Services.Setup;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;

namespace ExcPanel.TransferAgent.Tests.Setup;

public class JsonSetupStateStoreTests
{
    [Fact]
    public async Task SaveAsync_WritesAtomically()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"excpanel-setup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var environment = new Mock<IWebHostEnvironment>();
            environment.SetupGet(e => e.ContentRootPath).Returns(tempDir);

            var hostEnvironment = new Mock<IHostEnvironment>();
            hostEnvironment.Setup(e => e.EnvironmentName).Returns(Environments.Development);

            var options = Microsoft.Extensions.Options.Options.Create(new TransferAgentOptions { StateDirectory = tempDir });
            var store = new JsonSetupStateStore(environment.Object, options, hostEnvironment.Object);

            var state = await store.GetAsync();
            state.Status = SetupStatus.Running;
            state.CurrentStep = SetupStepNames.SystemPrerequisites;
            await store.SaveAsync(state);

            var reloaded = await store.GetAsync();
            Assert.Equal(SetupStatus.Running, reloaded.Status);
            Assert.Equal(SetupStepNames.SystemPrerequisites, reloaded.CurrentStep);
            Assert.True(File.Exists(Path.Combine(tempDir, "setup-state.json")));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }
}
