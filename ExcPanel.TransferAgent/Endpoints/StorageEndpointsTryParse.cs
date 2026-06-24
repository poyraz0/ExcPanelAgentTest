using ExcPanel.TransferAgent.Models;

namespace ExcPanel.TransferAgent.Endpoints;

internal static class StorageEndpointsTryParse
{
    public static bool TryParseJobId(string? value, out Guid jobId, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            jobId = Guid.Empty;
            error = "jobId is required.";
            return false;
        }

        if (!Guid.TryParse(value.Trim(), out jobId) || jobId == Guid.Empty)
        {
            error = "jobId must be a valid GUID.";
            return false;
        }

        return true;
    }

    public static bool TryParseJobType(string? value, out JobDirectoryType jobType, out string? error)
    {
        error = null;
        jobType = default;

        if (string.IsNullOrWhiteSpace(value))
        {
            error = "jobType is required.";
            return false;
        }

        if (!Enum.TryParse(value.Trim(), ignoreCase: true, out jobType)
            || (jobType != JobDirectoryType.Export && jobType != JobDirectoryType.Import))
        {
            error = "jobType must be Export or Import.";
            return false;
        }

        return true;
    }
}
