using ExcPanel.TransferAgent.Contracts;
using ExcPanel.TransferAgent.Models;

namespace ExcPanel.TransferAgent.Endpoints;

public static class StorageEndpoints
{
    public static IEndpointRouteBuilder MapStorageEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/storage/status", async (
            IStorageService storageService,
            CancellationToken cancellationToken) =>
        {
            var status = await storageService.GetStatusAsync(cancellationToken);
            return Results.Ok(ApiResponse<StorageStatusResponse>.Ok(status));
        })
        .WithName("GetStorageStatus")
        .WithOpenApi()
        .WithTags("Storage");

        app.MapGet("/api/storage/disks", async (
            IStorageDiskDiscoveryService diskDiscoveryService,
            CancellationToken cancellationToken) =>
        {
            var discovery = await diskDiscoveryService.DiscoverDisksAsync(cancellationToken);
            return Results.Ok(ApiResponse<StorageDiskDiscoveryResponse>.Ok(discovery));
        })
        .WithName("GetStorageDisks")
        .WithOpenApi()
        .WithTags("Storage");

        app.MapPost("/api/storage/configure/dry-run", async (
            StorageConfigureDryRunRequest request,
            IStorageConfigureService configureService,
            CancellationToken cancellationToken) =>
        {
            var result = await configureService.DryRunAsync(request, cancellationToken);
            return Results.Ok(ApiResponse<StorageConfigureDryRunResponse>.Ok(result));
        })
        .WithName("StorageConfigureDryRun")
        .WithOpenApi()
        .WithTags("Storage");

        // DESTRUCTIVE: partitions, formats disk and updates /etc/fstab.
        app.MapPost("/api/storage/configure", async (
            ConfigureStorageRequest request,
            IStorageConfigureService configureService,
            CancellationToken cancellationToken) =>
        {
            var result = await configureService.ConfigureAsync(request, cancellationToken);

            return result.Status switch
            {
                ConfigureStorageStatus.NotSupported => Results.Json(
                    new ApiResponse<ErrorResponse>
                    {
                        Success = false,
                        Data = new ErrorResponse { Message = result.Message ?? "Not supported." },
                        Message = result.Message
                    },
                    statusCode: StatusCodes.Status501NotImplemented),

                ConfigureStorageStatus.ValidationFailed => Results.Json(
                    new ApiResponse<ConfigureStorageFailureResponse>
                    {
                        Success = false,
                        Data = result.Failure,
                        Message = result.Message
                    },
                    statusCode: StatusCodes.Status400BadRequest),

                ConfigureStorageStatus.CommandFailed => Results.Json(
                    new ApiResponse<ConfigureStorageFailureResponse>
                    {
                        Success = false,
                        Data = result.Failure,
                        Message = result.Message
                    },
                    statusCode: StatusCodes.Status500InternalServerError),

                _ => Results.Ok(ApiResponse<ConfigureStorageResponse>.Ok(result.Success!))
            };
        })
        .WithName("StorageConfigure")
        .WithOpenApi()
        .WithTags("Storage");

        return app;
    }
}
