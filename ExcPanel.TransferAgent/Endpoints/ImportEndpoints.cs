using ExcPanel.TransferAgent.Contracts;
using ExcPanel.TransferAgent.Models;
using ExcPanel.TransferAgent.Models.Import;

namespace ExcPanel.TransferAgent.Endpoints;

public static class ImportEndpoints
{
    public static IEndpointRouteBuilder MapImportEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/import/prepare", async (
            ImportPrepareRequest request,
            IImportPrepareService importPrepareService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await importPrepareService.PrepareAsync(request, cancellationToken);
                return Results.Ok(ApiResponse<ImportPrepareResponse>.Ok(result, "Import prepare completed with warnings."));
            }
            catch (InvalidOperationException ex)
            {
                return Results.Json(
                    ApiResponse<ErrorResponse>.Fail(ex.Message),
                    statusCode: StatusCodes.Status400BadRequest);
            }
        })
        .WithName("ImportPrepare")
        .WithOpenApi()
        .WithTags("Import");

        return app;
    }
}
