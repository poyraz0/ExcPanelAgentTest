using ExcPanel.TransferAgent.Models;
using ExcPanel.TransferAgent.Models.Setup;
using ExcPanel.TransferAgent.Services.Setup;
using ExcPanel.TransferAgent.Tests.Fakes;

namespace ExcPanel.TransferAgent.Tests.Setup;

public class SetupConfirmationHelperTests
{
    [Fact]
    public void BuildDiskFormatConfirmationCode_UsesDiskTokenAndSize()
    {
        var disk = TestDiskFactory.CreateRecommendedDisk("/dev/sdb", 21474836480);
        var code = SetupConfirmationHelper.BuildDiskFormatConfirmationCode(disk);
        Assert.Equal("FORMAT-SDB-20G", code);
    }
}

public class MailboxFileNameSanitizerTests
{
    [Theory]
    [InlineData("ahmet.ertem@ofuzkal.com", "ofuzkal.com", "ahmet.ertem_ofuzkal.com.pst")]
    [InlineData("user.name@domain.com", "domain.com", "user.name_domain.com.pst")]
    public void SanitizePstFileName_ProducesSafeName(string mailbox, string domain, string expected)
    {
        var result = Services.Export.MailboxFileNameSanitizer.SanitizePstFileName(mailbox, domain);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void TryValidateMailbox_RejectsInvalid()
    {
        Assert.False(Services.Export.MailboxFileNameSanitizer.TryValidateMailbox("not-an-email", out _));
    }
}
