using ExcPanel.TransferAgent.Contracts.Validation;

namespace ExcPanel.TransferAgent.Tests.Domain;

public class DomainHostNamingTests
{
    [Theory]
    [InlineData("sftp")]
    [InlineData("transfer01")]
    [InlineData("mail-gw01")]
    public void TryValidateComputerName_AcceptsValidNames(string computerName)
    {
        Assert.True(DomainHostNaming.TryValidateComputerName(computerName, out var error));
        Assert.Null(error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("toolongcomputername")]
    [InlineData("-bad")]
    [InlineData("bad-")]
    public void TryValidateComputerName_RejectsInvalidNames(string computerName)
    {
        Assert.False(DomainHostNaming.TryValidateComputerName(computerName, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void TryBuildFqdn_BuildsExpectedFqdn()
    {
        Assert.True(DomainHostNaming.TryBuildFqdn("sftp", "dogrumail-demo.com", out var fqdn, out var error));
        Assert.Null(error);
        Assert.Equal("sftp.dogrumail-demo.com", fqdn);
    }

    [Fact]
    public void ResolveUncHost_PrefersExplicitUncHost()
    {
        var host = DomainHostNaming.ResolveUncHost(
            "custom.example.com",
            "sftp",
            "dogrumail-demo.com");

        Assert.Equal("custom.example.com", host);
    }

    [Fact]
    public void ResolveUncHost_FallsBackToComputerNameAndDnsDomain()
    {
        var host = DomainHostNaming.ResolveUncHost(
            null,
            "sftp",
            "dogrumail-demo.com");

        Assert.Equal("sftp.dogrumail-demo.com", host);
    }
}
