using ExcPanel.TransferAgent.Contracts;
using ExcPanel.TransferAgent.Models;

namespace ExcPanel.TransferAgent.Endpoints;

public static class StorageEndpoints
{
    private const string JobDirectoryPreparedMessage = "Job directory prepared successfully.";

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

        app.MapPost("/api/storage/job-directories", async (
            CreateJobDirectoryRequest request,
            IJobDirectoryProvider jobDirectoryProvider,
            CancellationToken cancellationToken) =>
        {
            if (!TryParseJobId(request.JobId, out var jobId, out var jobIdError))
            {
                return Results.Json(
                    ApiResponse<ErrorResponse>.Fail(jobIdError!),
                    statusCode: StatusCodes.Status400BadRequest);
            }

            if (!TryParseJobType(request.JobType, out var jobType, out var jobTypeError))
            {
                return Results.Json(
                    ApiResponse<ErrorResponse>.Fail(jobTypeError!),
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var result = await jobDirectoryProvider.CreateAsync(jobId, jobType, cancellationToken);
            return MapJobDirectoryResult(
                result,
                successMessage: result.Data?.Created == true
                    ? JobDirectoryPreparedMessage
                    : JobDirectoryPreparedMessage);
        })
        .WithName("CreateJobDirectory")
        .WithOpenApi()
        .WithTags("Storage");

        app.MapGet("/api/storage/job-directories/{jobType}/{jobId}", async (
            string jobType,
            string jobId,
            IJobDirectoryProvider jobDirectoryProvider,
            CancellationToken cancellationToken) =>
        {
            if (!TryParseJobId(jobId, out var parsedJobId, out var jobIdError))
            {
                return Results.Json(
                    ApiResponse<ErrorResponse>.Fail(jobIdError!),
                    statusCode: StatusCodes.Status400BadRequest);
            }

            if (!TryParseJobType(jobType, out var parsedJobType, out var jobTypeError))
            {
                return Results.Json(
                    ApiResponse<ErrorResponse>.Fail(jobTypeError!),
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var result = await jobDirectoryProvider.GetAsync(parsedJobId, parsedJobType, cancellationToken);
            return MapJobDirectoryResult(result);
        })
        .WithName("GetJobDirectory")
        .WithOpenApi()
        .WithTags("Storage");

        app.MapDelete("/api/storage/job-directories/{jobType}/{jobId}", async (
            string jobType,
            string jobId,
            IJobDirectoryProvider jobDirectoryProvider,
            CancellationToken cancellationToken) =>
        {
            if (!TryParseJobId(jobId, out var parsedJobId, out var jobIdError))
            {
                return Results.Json(
                    ApiResponse<ErrorResponse>.Fail(jobIdError!),
                    statusCode: StatusCodes.Status400BadRequest);
            }

            if (!TryParseJobType(jobType, out var parsedJobType, out var jobTypeError))
            {
                return Results.Json(
                    ApiResponse<ErrorResponse>.Fail(jobTypeError!),
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var result = await jobDirectoryProvider.DeleteAsync(parsedJobId, parsedJobType, cancellationToken);
            return MapJobDirectoryResult(result, successMessage: "Job directory deleted successfully.");
        })
        .WithName("DeleteJobDirectory")
        .WithOpenApi()
        .WithTags("Storage");

        return app;
    }

    private static bool TryParseJobId(string? value, out Guid jobId, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            jobId = Guid.Empty;
            error = "jobId is required.";
            return false;
        }

        if (!Guid.TryParse(value.Trim(), out jobId) || jobId == Guid.Empty)
        {
            error = "jobId must be a valid GUID.";
            return false;
        }

        return true;
    }

    private static bool TryParseJobType(string? value, out JobDirectoryType jobType, out string? error)
    {
        error = null;
        jobType = default;

        if (string.IsNullOrWhiteSpace(value))
        {
            error = "jobType is required.";
            return false;
        }

        if (!Enum.TryParse(value.Trim(), ignoreCase: true, out jobType)
            || (jobType != JobDirectoryType.Export && jobType != JobDirectoryType.Import))
        {
            error = "jobType must be Export or Import.";
            return false;
        }

        return true;
    }

    private static IResult MapJobDirectoryResult<T>(
        JobDirectoryOperationResult<T> result,
        string? successMessage = null)
    {
        return result.Status switch
        {
            JobDirectoryOperationStatus.Success => Results.Ok(
                ApiResponse<T>.Ok(result.Data!, successMessage ?? result.Message)),

            JobDirectoryOperationStatus.BadRequest => Results.Json(
                ApiResponse<ErrorResponse>.Fail(result.Message!),
                statusCode: StatusCodes.Status400BadRequest),

            JobDirectoryOperationStatus.NotFound => Results.Json(
                ApiResponse<ErrorResponse>.Fail(result.Message!),
                statusCode: StatusCodes.Status404NotFound),

            JobDirectoryOperationStatus.Conflict => Results.Json(
                ApiResponse<ErrorResponse>.Fail(result.Message!),
                statusCode: StatusCodes.Status409Conflict),

            JobDirectoryOperationStatus.Forbidden => Results.Json(
                ApiResponse<ErrorResponse>.Fail(result.Message!),
                statusCode: StatusCodes.Status403Forbidden),

            JobDirectoryOperationStatus.ServiceUnavailable => Results.Json(
                ApiResponse<ErrorResponse>.Fail(result.Message!),
                statusCode: StatusCodes.Status503ServiceUnavailable),

            _ => Results.Json(
                ApiResponse<ErrorResponse>.Fail(result.Message ?? "Job directory operation failed."),
                statusCode: StatusCodes.Status500InternalServerError)
        };
    }
}
