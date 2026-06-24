using ExcPanel.TransferAgent.Contracts;
using ExcPanel.TransferAgent.Models;
using ExcPanel.TransferAgent.Models.Export;

namespace ExcPanel.TransferAgent.Endpoints;

public static class ExportEndpoints
{
    public static IEndpointRouteBuilder MapExportEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/export/prepare", async (
            ExportPrepareRequest request,
            IExportPrepareService exportPrepareService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await exportPrepareService.PrepareAsync(request, cancellationToken);
                return Results.Ok(ApiResponse<ExportPrepareResponse>.Ok(result, "Export path prepared successfully."));
            }
            catch (InvalidOperationException ex)
            {
                return Results.Json(
                    ApiResponse<ErrorResponse>.Fail(ex.Message),
                    statusCode: StatusCodes.Status400BadRequest);
            }
        })
        .WithName("ExportPrepare")
        .WithOpenApi()
        .WithTags("Export");

        return app;
    }
}
