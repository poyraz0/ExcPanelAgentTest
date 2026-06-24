using ExcPanel.TransferAgent.Common;
using ExcPanel.TransferAgent.Models;

namespace ExcPanel.TransferAgent.Services.Setup;

public static class SetupConfirmationHelper
{
    public static string BuildDiskFormatConfirmationCode(StorageDiskInfo disk)
    {
        var diskToken = disk.Path.Trim('/').Replace("dev/", "", StringComparison.Ordinal).ToUpperInvariant();
        var sizeGb = (int)Math.Round(ByteSizeHelper.ToGigabytes(disk.SizeBytes));
        if (sizeGb <= 0)
        {
            sizeGb = (int)Math.Round(disk.SizeGb);
        }

        return $"FORMAT-{diskToken}-{sizeGb}G";
    }
}
