using ExcPanel.TransferAgent.Contracts;
using ExcPanel.TransferAgent.Models;
using ExcPanel.TransferAgent.Options;
using Microsoft.Extensions.Options;
using System.Reflection;

namespace ExcPanel.TransferAgent.Endpoints;

public static class AgentEndpoints
{
    public static IEndpointRouteBuilder MapAgentEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/agent/health", (
            IOptions<TransferAgentOptions> options,
            IPlatformInfoService platformInfoService) =>
        {
            var agentOptions = options.Value;
            var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";

            var health = new AgentHealthResponse
            {
                AgentName = agentOptions.AgentName,
                AgentId = agentOptions.AgentId,
                Version = version,
                Status = "Healthy",
                Platform = platformInfoService.GetPlatformInfo(),
                StorageRootPath = agentOptions.StorageRootPath,
                SftpEnabled = agentOptions.EnableSftp,
                SambaEnabled = agentOptions.EnableSamba,
                UtcTime = DateTime.UtcNow
            };

            return Results.Ok(ApiResponse<AgentHealthResponse>.Ok(health));
        })
        .WithName("GetAgentHealth")
        .WithOpenApi()
        .WithTags("Agent");

        return app;
    }
}
