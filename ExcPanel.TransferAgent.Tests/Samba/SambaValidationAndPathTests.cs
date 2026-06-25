using ExcPanel.TransferAgent.Contracts.Validation;
using ExcPanel.TransferAgent.Models;
using ExcPanel.TransferAgent.Models.Setup;
using ExcPanel.TransferAgent.Options;
using ExcPanel.TransferAgent.Services.Samba;

namespace ExcPanel.TransferAgent.Tests.Samba;

public class SambaValidationTests
{
    [Theory]
    [InlineData("PSTTransfer$")]
    [InlineData("valid-share_01")]
    public void ValidateShareName_AcceptsValidNames(string shareName)
    {
        Assert.Empty(SambaValidationHelpers.ValidateShareName(shareName));
    }

    [Theory]
    [InlineData("")]
    [InlineData("../evil")]
    [InlineData("bad/share")]
    public void ValidateShareName_RejectsInvalidNames(string shareName)
    {
        Assert.NotEmpty(SambaValidationHelpers.ValidateShareName(shareName));
    }

    [Fact]
    public void NormalizeServerName_UsesConfiguredValue()
    {
        var normalized = SambaValidationHelpers.NormalizeServerName("transfer01", "host", "host.example.com");
        Assert.Equal("TRANSFER01", normalized);
    }

    [Fact]
    public void NormalizeServerName_FallsBackToFqdnHostPart()
    {
        var normalized = SambaValidationHelpers.NormalizeServerName(null, "linux-host", "transfer01.corp.local");
        Assert.Equal("TRANSFER01", normalized);
    }

    [Fact]
    public void ValidateStorageRootsMatch_RejectsMismatch()
    {
        var errors = SambaValidationHelpers.ValidateStorageRootsMatch("/data/a", "/data/b");
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void FormatSambaValidUsers_EscapesDomainGroup()
    {
        var value = SambaValidationHelpers.FormatSambaValidUsers(@"DOGRU\Exchange Trusted Subsystem");
        Assert.Equal(
            @"@exchange trusted subsystem +""DOGRU\\Exchange Trusted Subsystem""",
            value);
    }

    [Fact]
    public void FormatSambaValidUsers_IncludesWinbindUnixGroupAndAdGroup()
    {
        var value = SambaValidationHelpers.FormatSambaValidUsers(@"DOGRUMAIL-DEMO\Exchange Trusted Subsystem");
        Assert.Equal(
            @"@exchange trusted subsystem +""DOGRUMAIL-DEMO\\Exchange Trusted Subsystem""",
            value);
    }
}

public class SambaConfigBuilderTests
{
    [Fact]
    public void BuildShareConfig_IsIdempotentForSameOptions()
    {
        var options = new SambaOptions
        {
            ShareName = "PSTTransfer$",
            StorageRoot = "/data/excpanel-transfer",
            RequiredAdGroup = @"DOGRU\Exchange Trusted Subsystem",
            AllowGuest = false
        };

        var first = SambaConfigBuilder.BuildShareConfig(options);
        var second = SambaConfigBuilder.BuildShareConfig(options);

        Assert.Equal(first, second);
        Assert.Contains("guest ok = no", first);
        Assert.Contains("vfs objects = acl_xattr", first);
        Assert.DoesNotContain("valid users =", first);
    }

    [Fact]
    public void UpsertGlobalIncludeBlock_IsIdempotent()
    {
        const string mainConfig = "[global]\n   workgroup = TEST\n";
        const string includePath = "/etc/samba/excpanel-transfer.conf";

        var first = SambaConfigBuilder.UpsertGlobalIncludeBlock(mainConfig, includePath);
        var second = SambaConfigBuilder.UpsertGlobalIncludeBlock(first, includePath);

        Assert.Equal(first, second);
        Assert.Contains("# BEGIN EXCPANEL TRANSFER AGENT", first);
        Assert.DoesNotContain("excpanel-transfer-agent include", first);
    }
}

public class SambaPathServiceTests
{
    [Fact]
    public void TryBuildUncPath_BuildsExportUncPath()
    {
        var service = CreatePathService();
        var jobId = Guid.Parse("7f3a2b1c-0000-0000-0000-000000000001");

        var unc = service.TryBuildUncPath(
            jobId,
            JobDirectoryType.Export,
            "exports/7f3a2b1c-0000-0000-0000-000000000001",
            "/data/excpanel-transfer/exports/7f3a2b1c-0000-0000-0000-000000000001");

        Assert.NotNull(unc);
        Assert.Equal(@"\\TRANSFER01\PSTTransfer$\exports\7f3a2b1c-0000-0000-0000-000000000001", unc!.UncDirectory);
        Assert.Equal("mailbox.pst", unc.SuggestedFileName);
        Assert.Contains(@"\\TRANSFER01\PSTTransfer$\exports\7f3a2b1c-0000-0000-0000-000000000001\mailbox.pst", unc.UncFilePath);
    }

    [Fact]
    public void TryBuildUncPath_BuildsImportUncPath()
    {
        var service = CreatePathService();
        var jobId = Guid.Parse("8f3a2b1c-0000-0000-0000-000000000002");

        var unc = service.TryBuildUncPath(
            jobId,
            JobDirectoryType.Import,
            "imports/8f3a2b1c-0000-0000-0000-000000000002",
            "/data/excpanel-transfer/imports/8f3a2b1c-0000-0000-0000-000000000002");

        Assert.NotNull(unc);
        Assert.Equal("import", unc!.JobType);
        Assert.Contains(@"\imports\8f3a2b1c-0000-0000-0000-000000000002", unc.UncDirectory);
    }

    [Fact]
    public void TryBuildUncPath_UsesSetupUncHostOverAppsettingsServerName()
    {
        var configStore = new ExcPanel.TransferAgent.Tests.Fakes.FakeSetupConfigStore
        {
            Document = new SetupConfigDocument
            {
                Samba = new SetupSambaConfig
                {
                    ShareName = "PSTTransfer$",
                    UncHost = "sftp.dogrumail-demo.com",
                    RequiredAdGroup = @"DOGRUMAIL-DEMO\Exchange Trusted Subsystem"
                }
            }
        };

        var service = new SambaPathService(
            Microsoft.Extensions.Options.Options.Create(new SambaOptions
            {
                Enabled = true,
                ShareName = "PSTTransfer$",
                ServerName = "TRANSFER01",
                StorageRoot = "/data/excpanel-transfer",
                RequiredAdGroup = @"DOGRUMAIL-DEMO\Exchange Trusted Subsystem"
            }),
            Microsoft.Extensions.Options.Options.Create(new TransferAgentOptions
            {
                StorageRootPath = "/data/excpanel-transfer"
            }),
            configStore);

        var unc = service.TryBuildUncPath(
            Guid.Parse("7f3a2b1c-0000-0000-0000-000000000001"),
            JobDirectoryType.Export,
            "exports/7f3a2b1c-0000-0000-0000-000000000001",
            "/data/excpanel-transfer/exports/7f3a2b1c-0000-0000-0000-000000000001");

        Assert.NotNull(unc);
        Assert.StartsWith(@"\\SFTP.DOGRUMAIL-DEMO.COM\PSTTransfer$\", unc!.UncDirectory, StringComparison.OrdinalIgnoreCase);
    }

  private static SambaPathService CreatePathService() =>
        new(
            Microsoft.Extensions.Options.Options.Create(new SambaOptions
            {
                Enabled = true,
                ShareName = "PSTTransfer$",
                ServerName = "TRANSFER01",
                StorageRoot = "/data/excpanel-transfer",
                RequiredAdGroup = @"DOGRU\Exchange Trusted Subsystem"
            }),
            Microsoft.Extensions.Options.Options.Create(new TransferAgentOptions
            {
                StorageRootPath = "/data/excpanel-transfer"
            }),
            new ExcPanel.TransferAgent.Tests.Fakes.FakeSetupConfigStore
            {
                Document = new SetupConfigDocument
                {
                    Samba = new SetupSambaConfig { UncHost = "TRANSFER01" }
                }
            });
}
