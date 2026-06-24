using ExcPanel.TransferAgent.Contracts.Validation;
using ExcPanel.TransferAgent.Models;
using ExcPanel.TransferAgent.Options;
using Microsoft.Extensions.Options;

namespace ExcPanel.TransferAgent.Services.Samba;

public class SambaPathService
{
    private readonly SambaOptions _sambaOptions;
    private readonly TransferAgentOptions _agentOptions;

    public SambaPathService(IOptions<SambaOptions> sambaOptions, IOptions<TransferAgentOptions> agentOptions)
    {
        _sambaOptions = sambaOptions.Value;
        _agentOptions = agentOptions.Value;
    }

    public string ResolveServerName()
    {
        var fqdn = TryReadFqdn();
        return SambaValidationHelpers.NormalizeServerName(
            _sambaOptions.ServerName,
            Environment.MachineName,
            fqdn);
    }

    public string ResolveStorageRoot() => _sambaOptions.StorageRoot.Trim();

    public string ResolveShareName() => _sambaOptions.ShareName.Trim();

    public string BuildUncRoot()
    {
        var serverName = ResolveServerName();
        var shareName = ResolveShareName();
        return SambaValidationHelpers.BuildUncRoot(serverName, shareName);
    }

    public SambaUncPathResponse? TryBuildUncPath(Guid jobId, JobDirectoryType jobType, string relativePath, string physicalPath)
    {
        if (!_sambaOptions.Enabled)
        {
            return null;
        }

        var shareErrors = SambaValidationHelpers.ValidateShareName(_sambaOptions.ShareName);
        var serverErrors = SambaValidationHelpers.ValidateServerName(ResolveServerName());
        if (shareErrors.Count > 0 || serverErrors.Count > 0)
        {
            return null;
        }

        var uncRoot = BuildUncRoot();
        var uncDirectory = SambaValidationHelpers.BuildUncDirectory(uncRoot, relativePath);
        var suggestedFileName = jobType switch
        {
            JobDirectoryType.Export => "mailbox.pst",
            JobDirectoryType.Import => "mailbox.pst",
            _ => "mailbox.pst"
        };

        return new SambaUncPathResponse
        {
            JobType = jobType.ToString().ToLowerInvariant(),
            JobId = jobId.ToString("D"),
            PhysicalDirectory = physicalPath,
            UncDirectory = uncDirectory,
            SuggestedFileName = suggestedFileName,
            UncFilePath = SambaValidationHelpers.BuildUncFilePath(uncDirectory, suggestedFileName)
        };
    }

    public IReadOnlyList<string> ValidateOptions()
    {
        var errors = new List<string>();
        errors.AddRange(SambaValidationHelpers.ValidateShareName(_sambaOptions.ShareName));
        errors.AddRange(SambaValidationHelpers.ValidateServerName(ResolveServerName()));
        errors.AddRange(SambaValidationHelpers.ValidateStorageRoot(_sambaOptions.StorageRoot));
        errors.AddRange(SambaValidationHelpers.ValidateRequiredAdGroup(_sambaOptions.RequiredAdGroup));
        errors.AddRange(SambaValidationHelpers.ValidateProtocol(_sambaOptions.SmbMinProtocol, "smbMinProtocol"));
        errors.AddRange(SambaValidationHelpers.ValidateProtocol(_sambaOptions.SmbMaxProtocol, "smbMaxProtocol"));
        errors.AddRange(SambaValidationHelpers.ValidateStorageRootsMatch(
            _agentOptions.StorageRootPath,
            _sambaOptions.StorageRoot));

        if (_sambaOptions.AllowGuest)
        {
            errors.Add("Guest access must remain disabled for Samba shares.");
        }

        return errors;
    }

    private static string? TryReadFqdn()
    {
        try
        {
            return System.Net.Dns.GetHostEntry(Environment.MachineName).HostName;
        }
        catch
        {
            return null;
        }
    }
}
