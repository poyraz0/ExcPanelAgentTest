using ExcPanel.TransferAgent.Contracts;
using ExcPanel.TransferAgent.Models;
using ExcPanel.TransferAgent.Models.Import;
using ExcPanel.TransferAgent.Services.Export;
using ExcPanel.TransferAgent.Contracts.Validation;
using ExcPanel.TransferAgent.Services.Samba;

namespace ExcPanel.TransferAgent.Services.Import;

public class ImportPrepareService : IImportPrepareService
{
    private readonly IJobDirectoryProvider _jobDirectoryProvider;
    private readonly ISambaProvider _sambaProvider;
    private readonly SambaPathService _sambaPathService;

    public ImportPrepareService(
        IJobDirectoryProvider jobDirectoryProvider,
        ISambaProvider sambaProvider,
        SambaPathService sambaPathService)
    {
        _jobDirectoryProvider = jobDirectoryProvider;
        _sambaProvider = sambaProvider;
        _sambaPathService = sambaPathService;
    }

    public async Task<ImportPrepareResponse> PrepareAsync(ImportPrepareRequest request, CancellationToken cancellationToken = default)
    {
        var warnings = new List<string>
        {
            "Import prepare is not fully implemented. Upload-only SFTP user provisioning will be added in a future release."
        };

        if (!MailboxFileNameSanitizer.TryValidateMailbox(request.Mailbox, out var mailboxError))
        {
            throw new InvalidOperationException(mailboxError);
        }

        var jobId = Guid.TryParse(request.JobId, out var parsed) && parsed != Guid.Empty
            ? parsed
            : Guid.NewGuid();

        var createResult = await _jobDirectoryProvider.CreateAsync(jobId, JobDirectoryType.Import, cancellationToken);
        if (createResult.Status != JobDirectoryOperationStatus.Success || createResult.Data is null)
        {
            throw new InvalidOperationException(createResult.Message ?? "Failed to create import job directory.");
        }

        string? uncDirectory = null;
        string? exchangeFilePath = null;
        var uncResult = _sambaProvider.BuildUncPath(jobId, JobDirectoryType.Import);
        if (uncResult.Status == SambaOperationStatus.Success && uncResult.Data is not null)
        {
            uncDirectory = uncResult.Data.UncDirectory;
            exchangeFilePath = uncResult.Data.UncFilePath;
        }
        else
        {
            var uncRoot = _sambaPathService.BuildUncRoot();
            uncDirectory = SambaValidationHelpers.BuildUncDirectory(uncRoot, createResult.Data.RelativePath);
        }

        return new ImportPrepareResponse
        {
            JobId = jobId.ToString("D"),
            Mailbox = request.Mailbox,
            Domain = request.Domain,
            PhysicalDirectory = createResult.Data.PhysicalPath,
            UncDirectory = uncDirectory,
            ExchangeFilePath = exchangeFilePath,
            SftpPlaceholder = "Upload-only SFTP user will be provisioned in a future release.",
            ReadyForImport = false,
            Warnings = warnings
        };
    }
}
