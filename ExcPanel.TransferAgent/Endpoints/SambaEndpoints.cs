using ExcPanel.TransferAgent.Contracts;
using ExcPanel.TransferAgent.Models;

namespace ExcPanel.TransferAgent.Endpoints;

public static class SambaEndpoints
{
    public static IEndpointRouteBuilder MapSambaEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/samba/status", async (
            ISambaProvider sambaProvider,
            CancellationToken cancellationToken) =>
        {
            var status = await sambaProvider.GetStatusAsync(cancellationToken);
            return Results.Ok(ApiResponse<SambaStatusResponse>.Ok(status));
        })
        .WithName("GetSambaStatus")
        .WithOpenApi()
        .WithTags("Samba");

        app.MapGet("/api/samba/prerequisites", async (
            ISambaProvider sambaProvider,
            CancellationToken cancellationToken) =>
        {
            var prerequisites = await sambaProvider.GetPrerequisitesAsync(cancellationToken);
            return Results.Ok(ApiResponse<SambaPrerequisitesResponse>.Ok(prerequisites));
        })
        .WithName("GetSambaPrerequisites")
        .WithOpenApi()
        .WithTags("Samba");

        app.MapPost("/api/samba/initialize/dry-run", async (
            ISambaProvider sambaProvider,
            CancellationToken cancellationToken) =>
        {
            var plan = await sambaProvider.BuildConfigurationPlanAsync(cancellationToken);
            return Results.Ok(ApiResponse<SambaInitializeDryRunResponse>.Ok(plan));
        })
        .WithName("SambaInitializeDryRun")
        .WithOpenApi()
        .WithTags("Samba");

        app.MapPost("/api/samba/initialize", async (
            ISambaProvider sambaProvider,
            CancellationToken cancellationToken) =>
        {
            var result = await sambaProvider.InitializeAsync(cancellationToken);
            return MapResult(result, successMessage: "Samba initialized successfully.");
        })
        .WithName("SambaInitialize")
        .WithOpenApi()
        .WithTags("Samba");

        app.MapGet("/api/samba/unc-path/{jobType}/{jobId}", (
            string jobType,
            string jobId,
            ISambaProvider sambaProvider) =>
        {
            if (!StorageEndpointsTryParse.TryParseJobId(jobId, out var parsedJobId, out var jobIdError))
            {
                return Results.Json(
                    ApiResponse<ErrorResponse>.Fail(jobIdError!),
                    statusCode: StatusCodes.Status400BadRequest);
            }

            if (!StorageEndpointsTryParse.TryParseJobType(jobType, out var parsedJobType, out var jobTypeError))
            {
                return Results.Json(
                    ApiResponse<ErrorResponse>.Fail(jobTypeError!),
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var result = sambaProvider.BuildUncPath(parsedJobId, parsedJobType);
            return MapResult(result);
        })
        .WithName("GetSambaUncPath")
        .WithOpenApi()
        .WithTags("Samba");

        return app;
    }

    private static IResult MapResult<T>(SambaOperationResult<T> result, string? successMessage = null)
    {
        return result.Status switch
        {
            SambaOperationStatus.Success => Results.Ok(
                ApiResponse<T>.Ok(result.Data!, successMessage ?? result.Message)),

            SambaOperationStatus.BadRequest => Results.Json(
                ApiResponse<ErrorResponse>.Fail(result.Message!),
                statusCode: StatusCodes.Status400BadRequest),

            SambaOperationStatus.NotFound => Results.Json(
                ApiResponse<ErrorResponse>.Fail(result.Message!),
                statusCode: StatusCodes.Status404NotFound),

            SambaOperationStatus.Conflict => Results.Json(
                ApiResponse<ErrorResponse>.Fail(result.Message!),
                statusCode: StatusCodes.Status409Conflict),

            SambaOperationStatus.Forbidden => Results.Json(
                ApiResponse<ErrorResponse>.Fail(result.Message!),
                statusCode: StatusCodes.Status403Forbidden),

            SambaOperationStatus.NotImplemented => Results.Json(
                ApiResponse<ErrorResponse>.Fail(result.Message!),
                statusCode: StatusCodes.Status501NotImplemented),

            SambaOperationStatus.NotSupported => Results.Json(
                ApiResponse<ErrorResponse>.Fail(result.Message!),
                statusCode: StatusCodes.Status501NotImplemented),

            SambaOperationStatus.ServiceUnavailable => Results.Json(
                ApiResponse<ErrorResponse>.Fail(result.Message!),
                statusCode: StatusCodes.Status503ServiceUnavailable),

            SambaOperationStatus.ValidationFailed => Results.Json(
                ApiResponse<ErrorResponse>.Fail(result.Message!),
                statusCode: StatusCodes.Status400BadRequest),

            _ => Results.Json(
                ApiResponse<ErrorResponse>.Fail(result.Message ?? "Samba operation failed."),
                statusCode: StatusCodes.Status500InternalServerError)
        };
    }
}
