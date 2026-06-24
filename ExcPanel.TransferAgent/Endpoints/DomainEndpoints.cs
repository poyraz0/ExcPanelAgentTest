using ExcPanel.TransferAgent.Contracts;
using ExcPanel.TransferAgent.Models;
using ExcPanel.TransferAgent.Models.Domain;

namespace ExcPanel.TransferAgent.Endpoints;

public static class DomainEndpoints
{
    public static IEndpointRouteBuilder MapDomainEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/domain/status", async (
            IDomainService domainService,
            CancellationToken cancellationToken) =>
        {
            var status = await domainService.GetStatusAsync(cancellationToken);
            return Results.Ok(ApiResponse<DomainStatusResponse>.Ok(status));
        })
        .WithName("GetDomainStatus")
        .WithOpenApi()
        .WithTags("Domain");

        app.MapPost("/api/domain/precheck", async (
            DomainPrecheckRequest request,
            IDomainService domainService,
            CancellationToken cancellationToken) =>
        {
            var result = await domainService.PrecheckAsync(request, cancellationToken);
            return Results.Ok(ApiResponse<DomainPrecheckResponse>.Ok(result));
        })
        .WithName("DomainPrecheck")
        .WithOpenApi()
        .WithTags("Domain");

        app.MapPost("/api/domain/join", async (
            DomainJoinRequest request,
            IDomainService domainService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await domainService.JoinAsync(request, cancellationToken);
                return Results.Ok(ApiResponse<DomainJoinResponse>.Ok(result, "Domain join completed."));
            }
            catch (InvalidOperationException ex)
            {
                return Results.Json(
                    ApiResponse<ErrorResponse>.Fail(ex.Message),
                    statusCode: StatusCodes.Status400BadRequest);
            }
        })
        .WithName("DomainJoin")
        .WithOpenApi()
        .WithTags("Domain");

        app.MapPost("/api/domain/test", async (
            IDomainService domainService,
            CancellationToken cancellationToken) =>
        {
            var result = await domainService.TestAsync(cancellationToken);
            return Results.Ok(ApiResponse<DomainTestResponse>.Ok(result));
        })
        .WithName("DomainTest")
        .WithOpenApi()
        .WithTags("Domain");

        return app;
    }
}
