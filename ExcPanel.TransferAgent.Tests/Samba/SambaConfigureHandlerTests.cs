using System.Text.Json;
using ExcPanel.TransferAgent.Contracts;
using ExcPanel.TransferAgent.PrivilegedHelper.Handlers;
using ExcPanel.TransferAgent.Tests.Fakes;

namespace ExcPanel.TransferAgent.Tests.Samba;

public class SambaConfigureHandlerTests
{
    [Fact]
    public async Task HandleAsync_RollsBackWhenTestparmFails()
    {
        var runner = new FakePrivilegedCommandRunner();
        runner.SetHandler("findmnt", _ => new CommandExecutionResult { ExitCode = 0 });
        runner.SetHandler("getent", _ => new CommandExecutionResult { ExitCode = 0, Stdout = "group:x:1:" });
        runner.SetHandler("setfacl", _ => new CommandExecutionResult { ExitCode = 0 });
        runner.SetHandler("testparm", _ => new CommandExecutionResult { ExitCode = 1, Stderr = "invalid config" });
        runner.SetHandler("systemctl", _ => new CommandExecutionResult { ExitCode = 0 });

        var storageRoot = Path.Combine(Path.GetTempPath(), $"excpanel-samba-{Guid.NewGuid():N}");
        Directory.CreateDirectory(storageRoot);

        var configPath = Path.Combine(storageRoot, "excpanel-transfer.conf");
        var mainConfigPath = Path.Combine(storageRoot, "smb.conf");
        await File.WriteAllTextAsync(mainConfigPath, "[global]\n");

        try
        {
            var handler = new SambaConfigureHandler(runner);
            var payload = JsonSerializer.SerializeToElement(new SambaConfigurePayload
            {
                ShareName = "PSTTransfer$",
                StorageRoot = storageRoot,
                ConfigFilePath = configPath,
                MainConfigPath = mainConfigPath,
                RequiredAdGroup = @"DOGRU\Exchange Trusted Subsystem",
                AllowGuest = false
            });

            var response = await handler.HandleAsync("req-1", payload, CancellationToken.None);

            Assert.False(response.Success);
            Assert.Equal(PrivilegedHelperErrorCodes.SambaValidationFailed, response.ErrorCode);
            Assert.Contains("RollbackConfiguration", response.CompletedSteps);
            Assert.False(File.Exists(configPath));
            Assert.Equal("[global]\n", await File.ReadAllTextAsync(mainConfigPath));
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
    public async Task HandleAsync_ProducesGuestDisabledConfig()
    {
        var runner = new FakePrivilegedCommandRunner();
        runner.SetHandler("findmnt", _ => new CommandExecutionResult { ExitCode = 0 });
        runner.SetHandler("getent", _ => new CommandExecutionResult { ExitCode = 0, Stdout = "group:x:1:" });
        runner.SetHandler("setfacl", _ => new CommandExecutionResult { ExitCode = 0 });
        runner.SetHandler("testparm", _ => new CommandExecutionResult
        {
            ExitCode = 0,
            Stdout = "[PSTTransfer$]\n"
        });
        runner.SetHandler("systemctl", _ => new CommandExecutionResult { ExitCode = 0 });

        var storageRoot = Path.Combine(Path.GetTempPath(), $"excpanel-samba-{Guid.NewGuid():N}");
        Directory.CreateDirectory(storageRoot);
        var configPath = Path.Combine(storageRoot, "excpanel-transfer.conf");
        var mainConfigPath = Path.Combine(storageRoot, "smb.conf");
        await File.WriteAllTextAsync(mainConfigPath, "[global]\n");

        try
        {
            var handler = new SambaConfigureHandler(runner);
            var payload = JsonSerializer.SerializeToElement(new SambaConfigurePayload
            {
                ShareName = "PSTTransfer$",
                StorageRoot = storageRoot,
                ConfigFilePath = configPath,
                MainConfigPath = mainConfigPath,
                RequiredAdGroup = @"DOGRU\Exchange Trusted Subsystem",
                AllowGuest = false
            });

            var response = await handler.HandleAsync("req-2", payload, CancellationToken.None);

            Assert.True(response.Success);
            var content = await File.ReadAllTextAsync(configPath);
            Assert.Contains("guest ok = no", content);
            Assert.DoesNotContain("guest ok = yes", content);
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
    public async Task HandleAsync_FailsWhenEffectiveShareMissing()
    {
        var runner = new FakePrivilegedCommandRunner();
        runner.SetHandler("findmnt", _ => new CommandExecutionResult { ExitCode = 0 });
        runner.SetHandler("getent", _ => new CommandExecutionResult { ExitCode = 0, Stdout = "group:x:1:" });
        runner.SetHandler("setfacl", _ => new CommandExecutionResult { ExitCode = 0 });
        runner.SetHandler("testparm", _ => new CommandExecutionResult
        {
            ExitCode = 0,
            Stdout = "[global]\n   workgroup = EXAMPLE\n"
        });
        runner.SetHandler("systemctl", _ => new CommandExecutionResult { ExitCode = 0 });

        var storageRoot = Path.Combine(Path.GetTempPath(), $"excpanel-samba-{Guid.NewGuid():N}");
        Directory.CreateDirectory(storageRoot);
        var configPath = Path.Combine(storageRoot, "excpanel-transfer.conf");
        var mainConfigPath = Path.Combine(storageRoot, "smb.conf");
        await File.WriteAllTextAsync(mainConfigPath, "[global]\n");

        try
        {
            var handler = new SambaConfigureHandler(runner);
            var payload = JsonSerializer.SerializeToElement(new SambaConfigurePayload
            {
                ShareName = "PSTTransfer$",
                StorageRoot = storageRoot,
                ConfigFilePath = configPath,
                MainConfigPath = mainConfigPath,
                RequiredAdGroup = @"DOGRU\Exchange Trusted Subsystem",
                AllowGuest = false
            });

            var response = await handler.HandleAsync("req-3", payload, CancellationToken.None);

            Assert.False(response.Success);
            Assert.Equal(PrivilegedHelperErrorCodes.SambaValidationFailed, response.ErrorCode);
            Assert.Equal("ValidateWithTestparm", response.FailedStep);
            Assert.Contains("RollbackConfiguration", response.CompletedSteps);
            Assert.False(File.Exists(configPath));
            Assert.Equal("[global]\n", await File.ReadAllTextAsync(mainConfigPath));
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
    public async Task HandleAsync_WritesManagedIncludeBlockUnderGlobal()
    {
        var runner = new FakePrivilegedCommandRunner();
        runner.SetHandler("findmnt", _ => new CommandExecutionResult { ExitCode = 0 });
        runner.SetHandler("getent", _ => new CommandExecutionResult { ExitCode = 0, Stdout = "group:x:1:" });
        runner.SetHandler("setfacl", _ => new CommandExecutionResult { ExitCode = 0 });
        runner.SetHandler("testparm", _ => new CommandExecutionResult
        {
            ExitCode = 0,
            Stdout = "[PSTTransfer$]\n"
        });
        runner.SetHandler("systemctl", _ => new CommandExecutionResult { ExitCode = 0 });

        var storageRoot = Path.Combine(Path.GetTempPath(), $"excpanel-samba-{Guid.NewGuid():N}");
        Directory.CreateDirectory(storageRoot);
        var configPath = Path.Combine(storageRoot, "excpanel-transfer.conf");
        var mainConfigPath = Path.Combine(storageRoot, "smb.conf");
        const string mainConfig = """
[global]
   workgroup = EXAMPLE

[print$]
   path = /var/spool/samba
   include = /etc/samba/excpanel-transfer.conf # excpanel-transfer-agent include
""";
        await File.WriteAllTextAsync(mainConfigPath, mainConfig);

        try
        {
            var handler = new SambaConfigureHandler(runner);
            var payload = JsonSerializer.SerializeToElement(new SambaConfigurePayload
            {
                ShareName = "PSTTransfer$",
                StorageRoot = storageRoot,
                ConfigFilePath = configPath,
                MainConfigPath = mainConfigPath,
                RequiredAdGroup = @"DOGRU\Exchange Trusted Subsystem",
                AllowGuest = false
            });

            var response = await handler.HandleAsync("req-4", payload, CancellationToken.None);

            Assert.True(response.Success);
            var updatedMain = await File.ReadAllTextAsync(mainConfigPath);
            Assert.Contains("# BEGIN EXCPANEL TRANSFER AGENT", updatedMain);
            Assert.DoesNotContain("excpanel-transfer-agent include", updatedMain);
            Assert.DoesNotContain("[print$]\n   path = /var/spool/samba\n   include =", updatedMain, StringComparison.Ordinal);
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
