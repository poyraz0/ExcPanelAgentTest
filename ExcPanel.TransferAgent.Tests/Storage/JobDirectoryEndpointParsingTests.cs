using ExcPanel.TransferAgent.Endpoints;
using ExcPanel.TransferAgent.Models;
using ExcPanel.TransferAgent.Options;
using ExcPanel.TransferAgent.Services.Samba;
using Microsoft.Extensions.Options;

namespace ExcPanel.TransferAgent.Tests.Storage;

public class JobDirectoryEndpointParsingTests
{
    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void TryParseJobId_RejectsInvalidValues(string value)
    {
        var parsed = StorageEndpointsTryParse.TryParseJobId(value, out var jobId, out var error);

        Assert.False(parsed);
        Assert.NotNull(error);
        Assert.Equal(Guid.Empty, jobId);
    }

    [Theory]
    [InlineData("Export", JobDirectoryType.Export)]
    [InlineData("import", JobDirectoryType.Import)]
    public void TryParseJobType_AcceptsValidValues(string value, JobDirectoryType expected)
    {
        var parsed = StorageEndpointsTryParse.TryParseJobType(value, out var jobType, out var error);

        Assert.True(parsed);
        Assert.Null(error);
        Assert.Equal(expected, jobType);
    }

    [Theory]
    [InlineData("Backup")]
    [InlineData("temp")]
    public void TryParseJobType_RejectsInvalidSambaJobTypes(string value)
    {
        var parsed = StorageEndpointsTryParse.TryParseJobType(value, out _, out var error);

        Assert.False(parsed);
        Assert.NotNull(error);
    }
}
