using ExcPanel.TransferAgent.Contracts;
using ExcPanel.TransferAgent.Models;

namespace ExcPanel.TransferAgent.Endpoints;

public static class SftpEndpoints
{
    public static IEndpointRouteBuilder MapSftpEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/sftp/initialize", async (
            ISftpService sftpService,
            CancellationToken cancellationToken) =>
        {
            var result = await sftpService.InitializeAsync(cancellationToken);
            return MapResult(result, successMessage: "SFTP initialized successfully.");
        })
        .WithName("InitializeSftp")
        .WithOpenApi()
        .WithTags("Sftp");

        app.MapGet("/api/sftp/status", async (
            ISftpService sftpService,
            CancellationToken cancellationToken) =>
        {
            var result = await sftpService.GetStatusAsync(cancellationToken);
            return MapResult(result);
        })
        .WithName("GetSftpStatus")
        .WithOpenApi()
        .WithTags("Sftp");

        app.MapPost("/api/sftp/users", async (
            CreateSftpUserRequest request,
            ISftpService sftpService,
            CancellationToken cancellationToken) =>
        {
            var result = await sftpService.CreateUserAsync(request, cancellationToken);
            return MapResult(result, successMessage: "SFTP user created successfully.");
        })
        .WithName("CreateSftpUser")
        .WithOpenApi()
        .WithTags("Sftp");

        app.MapGet("/api/sftp/users/{username}", async (
            string username,
            ISftpService sftpService,
            CancellationToken cancellationToken) =>
        {
            var result = await sftpService.GetUserAsync(username, cancellationToken);
            return MapResult(result);
        })
        .WithName("GetSftpUser")
        .WithOpenApi()
        .WithTags("Sftp");

        app.MapPost("/api/sftp/users/{username}/disable", async (
            string username,
            ISftpService sftpService,
            CancellationToken cancellationToken) =>
        {
            var result = await sftpService.DisableUserAsync(username, cancellationToken);
            return MapResult(result, successMessage: "SFTP user disabled successfully.");
        })
        .WithName("DisableSftpUser")
        .WithOpenApi()
        .WithTags("Sftp");

        app.MapDelete("/api/sftp/users/{username}", async (
            string username,
            ISftpService sftpService,
            CancellationToken cancellationToken) =>
        {
            var result = await sftpService.DeleteUserAsync(username, cancellationToken);
            return MapResult(result, successMessage: "SFTP user deleted successfully.");
        })
        .WithName("DeleteSftpUser")
        .WithOpenApi()
        .WithTags("Sftp");

        return app;
    }

    private static IResult MapResult<T>(SftpOperationResult<T> result, string? successMessage = null)
    {
        return result.Status switch
        {
            SftpOperationStatus.Success => Results.Ok(ApiResponse<T>.Ok(result.Data!, successMessage ?? result.Message)),
            SftpOperationStatus.BadRequest => Results.Json(
                ApiResponse<ErrorResponse>.Fail(result.Message!),
                statusCode: StatusCodes.Status400BadRequest),
            SftpOperationStatus.NotFound => Results.Json(
                ApiResponse<ErrorResponse>.Fail(result.Message!),
                statusCode: StatusCodes.Status404NotFound),
            SftpOperationStatus.Conflict => Results.Json(
                ApiResponse<ErrorResponse>.Fail(result.Message!),
                statusCode: StatusCodes.Status409Conflict),
            SftpOperationStatus.Forbidden => Results.Json(
                ApiResponse<ErrorResponse>.Fail(result.Message!),
                statusCode: StatusCodes.Status403Forbidden),
            SftpOperationStatus.NotImplemented => Results.Json(
                ApiResponse<ErrorResponse>.Fail(result.Message!),
                statusCode: StatusCodes.Status501NotImplemented),
            SftpOperationStatus.NotSupported => Results.Json(
                ApiResponse<ErrorResponse>.Fail(result.Message!),
                statusCode: StatusCodes.Status501NotImplemented),
            SftpOperationStatus.ServiceUnavailable => Results.Json(
                ApiResponse<ErrorResponse>.Fail(result.Message!),
                statusCode: StatusCodes.Status503ServiceUnavailable),
            _ => Results.Json(
                ApiResponse<ErrorResponse>.Fail(result.Message ?? "SFTP operation failed."),
                statusCode: StatusCodes.Status500InternalServerError)
        };
    }
}
