using ExcPanel.TransferAgent.Contracts;
using ExcPanel.TransferAgent.Models;
using ExcPanel.TransferAgent.Models.Export;
using ExcPanel.TransferAgent.Models.Setup;

namespace ExcPanel.TransferAgent.Endpoints;

public static class SetupEndpoints
{
    public static IEndpointRouteBuilder MapSetupEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/setup/status", async (
            ISetupOrchestrationService setupService,
            CancellationToken cancellationToken) =>
        {
            var status = await setupService.GetStatusAsync(cancellationToken);
            return Results.Ok(ApiResponse<SetupStatusResponse>.Ok(status));
        })
        .WithName("GetSetupStatus")
        .WithOpenApi()
        .WithTags("Setup");

        app.MapGet("/api/setup/prerequisites", async (
            ISetupPrerequisitesService prerequisitesService,
            CancellationToken cancellationToken) =>
        {
            var prerequisites = await prerequisitesService.CheckAsync(cancellationToken);
            return Results.Ok(ApiResponse<SetupPrerequisitesResponse>.Ok(prerequisites));
        })
        .WithName("GetSetupPrerequisites")
        .WithOpenApi()
        .WithTags("Setup");

        app.MapPost("/api/setup/plan", async (
            SetupPlanRequest request,
            ISetupOrchestrationService setupService,
            CancellationToken cancellationToken) =>
        {
            var plan = await setupService.PlanAsync(request, cancellationToken);
            return Results.Ok(ApiResponse<SetupPlanResponse>.Ok(plan));
        })
        .WithName("SetupPlan")
        .WithOpenApi()
        .WithTags("Setup");

        app.MapPost("/api/setup/apply", async (
            SetupApplyRequest request,
            ISetupOrchestrationService setupService,
            CancellationToken cancellationToken) =>
        {
            var result = await setupService.ApplyAsync(request, cancellationToken);
            return Results.Ok(ApiResponse<SetupApplyResponse>.Ok(result));
        })
        .WithName("SetupApply")
        .WithOpenApi()
        .WithTags("Setup");

        app.MapPost("/api/setup/validate-export-path", async (
            SetupValidateExportPathRequest request,
            IExportPrepareService exportPrepareService,
            CancellationToken cancellationToken) =>
        {
            if (!Guid.TryParse(request.JobId, out var jobId) || jobId == Guid.Empty)
            {
                return Results.Json(
                    ApiResponse<ErrorResponse>.Fail("jobId must be a valid GUID."),
                    statusCode: StatusCodes.Status400BadRequest);
            }

            try
            {
                var result = await exportPrepareService.PrepareAsync(new ExportPrepareRequest
                {
                    JobId = request.JobId,
                    Mailbox = request.Mailbox,
                    Domain = request.Domain
                }, cancellationToken);

                return Results.Ok(ApiResponse<object>.Ok(new
                {
                    result.JobId,
                    physicalDirectory = result.PhysicalDirectory,
                    uncDirectory = result.UncDirectory,
                    pstFileName = result.PstFileName,
                    exchangeFilePath = result.ExchangeFilePath,
                    readyForExchange = result.ReadyForExchangeExport
                }));
            }
            catch (InvalidOperationException ex)
            {
                return Results.Json(
                    ApiResponse<ErrorResponse>.Fail(ex.Message),
                    statusCode: StatusCodes.Status400BadRequest);
            }
        })
        .WithName("SetupValidateExportPath")
        .WithOpenApi()
        .WithTags("Setup");

        app.MapPost("/api/setup/test-samba-write", async (
            ISetupOrchestrationService setupService,
            CancellationToken cancellationToken) =>
        {
            var result = await setupService.TestSambaWriteAsync(cancellationToken);
            return Results.Ok(ApiResponse<SetupSambaWriteTestResponse>.Ok(result));
        })
        .WithName("SetupTestSambaWrite")
        .WithOpenApi()
        .WithTags("Setup");

        app.MapGet("/api/setup/config", async (
            ISetupConfigService configService,
            CancellationToken cancellationToken) =>
        {
            var config = await configService.GetConfigAsync(cancellationToken);
            return Results.Ok(ApiResponse<SetupConfigDocument>.Ok(config));
        })
        .WithName("GetSetupConfig")
        .WithOpenApi()
        .WithTags("Setup");

        app.MapPut("/api/setup/config/storage", async (
            SetupStorageConfig config,
            ISetupConfigService configService,
            CancellationToken cancellationToken) =>
        {
            var updated = await configService.UpdateStorageAsync(config, cancellationToken);
            return Results.Ok(ApiResponse<SetupConfigDocument>.Ok(updated, "Storage configuration updated."));
        })
        .WithName("UpdateSetupStorageConfig")
        .WithOpenApi()
        .WithTags("Setup");

        app.MapPut("/api/setup/config/domain", async (
            SetupDomainConfig config,
            ISetupConfigService configService,
            CancellationToken cancellationToken) =>
        {
            var updated = await configService.UpdateDomainAsync(config, cancellationToken);
            return Results.Ok(ApiResponse<SetupConfigDocument>.Ok(updated, "Domain configuration updated."));
        })
        .WithName("UpdateSetupDomainConfig")
        .WithOpenApi()
        .WithTags("Setup");

        app.MapPut("/api/setup/config/samba", async (
            SetupSambaConfig config,
            ISetupConfigService configService,
            CancellationToken cancellationToken) =>
        {
            var updated = await configService.UpdateSambaAsync(config, cancellationToken);
            return Results.Ok(ApiResponse<SetupConfigDocument>.Ok(updated, "Samba configuration updated."));
        })
        .WithName("UpdateSetupSambaConfig")
        .WithOpenApi()
        .WithTags("Setup");

        app.MapPut("/api/setup/config/sftp", async (
            SetupSftpConfig config,
            ISetupConfigService configService,
            CancellationToken cancellationToken) =>
        {
            var updated = await configService.UpdateSftpAsync(config, cancellationToken);
            return Results.Ok(ApiResponse<SetupConfigDocument>.Ok(updated, "SFTP configuration updated."));
        })
        .WithName("UpdateSetupSftpConfig")
        .WithOpenApi()
        .WithTags("Setup");

        return app;
    }
}
