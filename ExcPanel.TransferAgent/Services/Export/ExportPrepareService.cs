using System.Runtime.InteropServices;
using System.Text.Json;
using ExcPanel.TransferAgent.Contracts;
using ExcPanel.TransferAgent.Models;
using ExcPanel.TransferAgent.Models.Domain;
using ExcPanel.TransferAgent.Models.Export;
using ExcPanel.TransferAgent.Options;
using ExcPanel.TransferAgent.Services.Export;
using ExcPanel.TransferAgent.Contracts.Validation;
using ExcPanel.TransferAgent.Services.Samba;
using Microsoft.Extensions.Options;

namespace ExcPanel.TransferAgent.Services.Export;

public class ExportPrepareService : IExportPrepareService
{
    private readonly TransferAgentOptions _agentOptions;
    private readonly SambaOptions _sambaOptions;
    private readonly SetupOptions _setupOptions;
    private readonly IJobDirectoryProvider _jobDirectoryProvider;
    private readonly IExchangeAclService _exchangeAclService;
    private readonly ISambaProvider _sambaProvider;
    private readonly SambaPathService _sambaPathService;
    private readonly IStorageService _storageService;
    private readonly IStorageMountChecker _mountChecker;

    public ExportPrepareService(
        IOptions<TransferAgentOptions> agentOptions,
        IOptions<SambaOptions> sambaOptions,
        IOptions<SetupOptions> setupOptions,
        IJobDirectoryProvider jobDirectoryProvider,
        IExchangeAclService exchangeAclService,
        ISambaProvider sambaProvider,
        SambaPathService sambaPathService,
        IStorageService storageService,
        IStorageMountChecker mountChecker)
    {
        _agentOptions = agentOptions.Value;
        _sambaOptions = sambaOptions.Value;
        _setupOptions = setupOptions.Value;
        _jobDirectoryProvider = jobDirectoryProvider;
        _exchangeAclService = exchangeAclService;
        _sambaProvider = sambaProvider;
        _sambaPathService = sambaPathService;
        _storageService = storageService;
        _mountChecker = mountChecker;
    }

    public async Task<ExportPrepareResponse> PrepareAsync(ExportPrepareRequest request, CancellationToken cancellationToken = default)
    {
        var warnings = new List<string>();

        if (!MailboxFileNameSanitizer.TryValidateMailbox(request.Mailbox, out var mailboxError))
        {
            throw new InvalidOperationException(mailboxError);
        }

        if (string.IsNullOrWhiteSpace(request.Domain))
        {
            throw new InvalidOperationException("domain is required.");
        }

        var jobId = Guid.TryParse(request.JobId, out var parsed) && parsed != Guid.Empty
            ? parsed
            : Guid.NewGuid();

        var pstFileName = MailboxFileNameSanitizer.SanitizePstFileName(request.Mailbox, request.Domain);

        var createResult = await _jobDirectoryProvider.CreateAsync(jobId, JobDirectoryType.Export, cancellationToken);
        if (createResult.Status != JobDirectoryOperationStatus.Success || createResult.Data is null)
        {
            throw new InvalidOperationException(createResult.Message ?? "Failed to create export job directory.");
        }

        var aclResult = await _exchangeAclService.ApplyExchangeAclAsync(
            createResult.Data.PhysicalPath,
            _sambaOptions.RequiredAdGroup,
            cancellationToken);
        if (!aclResult.Success)
        {
            warnings.Add(aclResult.Message ?? "Failed to apply Exchange ACL.");
        }

        var uncResult = _sambaProvider.BuildUncPath(jobId, JobDirectoryType.Export);
        string uncDirectory;
        string exchangeFilePath;

        if (uncResult.Status == SambaOperationStatus.Success && uncResult.Data is not null)
        {
            uncDirectory = uncResult.Data.UncDirectory;
            exchangeFilePath = SambaValidationHelpers.BuildUncFilePath(uncDirectory, pstFileName);
        }
        else
        {
            var relativePath = createResult.Data.RelativePath;
            var uncRoot = _sambaPathService.BuildUncRoot();
            uncDirectory = SambaValidationHelpers.BuildUncDirectory(uncRoot, relativePath);
            exchangeFilePath = SambaValidationHelpers.BuildUncFilePath(uncDirectory, pstFileName);
            warnings.Add("UNC path built from configuration; Samba validation reported issues.");
        }

        var storageStatus = await _storageService.GetStatusAsync(cancellationToken);
        if (request.EstimatedMailboxSizeGb.HasValue && storageStatus.FreeGb.HasValue)
        {
            if (storageStatus.FreeGb.Value < request.EstimatedMailboxSizeGb.Value)
            {
                warnings.Add($"Storage free space ({storageStatus.FreeGb:F2} GB) may be insufficient for estimated mailbox size ({request.EstimatedMailboxSizeGb} GB).");
            }
        }

        if (!await _mountChecker.IsMountedAsync(_agentOptions.StorageRootPath, cancellationToken))
        {
            warnings.Add("Storage root is not mounted.");
        }

        return new ExportPrepareResponse
        {
            JobId = jobId.ToString("D"),
            Mailbox = request.Mailbox,
            Domain = request.Domain,
            PhysicalDirectory = createResult.Data.PhysicalPath,
            UncDirectory = uncDirectory,
            PstFileName = pstFileName,
            ExchangeFilePath = exchangeFilePath,
            ReadyForExchangeExport = warnings.Count == 0 || !warnings.Any(w => w.Contains("not mounted", StringComparison.OrdinalIgnoreCase)),
            Warnings = warnings
        };
    }
}
