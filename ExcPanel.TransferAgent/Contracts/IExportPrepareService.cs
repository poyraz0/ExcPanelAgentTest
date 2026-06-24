using ExcPanel.TransferAgent.Models.Export;

namespace ExcPanel.TransferAgent.Contracts;

public interface IExportPrepareService
{
    Task<ExportPrepareResponse> PrepareAsync(ExportPrepareRequest request, CancellationToken cancellationToken = default);
}
