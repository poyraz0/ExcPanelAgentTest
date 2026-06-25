using ExcPanel.TransferAgent.Contracts.Validation;

namespace ExcPanel.TransferAgent.Tests.Samba;

public class ExchangeAclHelpersTests
{
    [Fact]
    public void BuildModifyAccessArguments_UsesDomainGroupFormat()
    {
        var args = ExchangeAclHelpers.BuildModifyAccessArguments(
            "/data/excpanel-transfer/exports/job",
            @"DOGRUMAIL-DEMO\Exchange Trusted Subsystem");

        Assert.Equal(["-m", @"g:DOGRUMAIL-DEMO\Exchange Trusted Subsystem:rwx", "/data/excpanel-transfer/exports/job"], args);
    }

    [Fact]
    public void BuildRecursiveModifyDefaultArguments_IncludesRecursiveFlag()
    {
        var args = ExchangeAclHelpers.BuildRecursiveModifyDefaultArguments(
            "/data/excpanel-transfer",
            @"DOGRUMAIL-DEMO\Exchange Trusted Subsystem");

        Assert.Equal(
            ["-R", "-d", "-m", @"g:DOGRUMAIL-DEMO\Exchange Trusted Subsystem:rwx", "/data/excpanel-transfer"],
            args);
    }
}
