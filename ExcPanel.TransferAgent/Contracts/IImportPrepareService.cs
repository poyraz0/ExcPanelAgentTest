using ExcPanel.TransferAgent.Models.Import;

namespace ExcPanel.TransferAgent.Contracts;

public interface IImportPrepareService
{
    Task<ImportPrepareResponse> PrepareAsync(ImportPrepareRequest request, CancellationToken cancellationToken = default);
}
