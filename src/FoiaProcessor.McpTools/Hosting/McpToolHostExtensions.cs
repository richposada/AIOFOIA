using FoiaProcessor.McpTools.Servers;
using Microsoft.Extensions.DependencyInjection;

namespace FoiaProcessor.McpTools.Hosting;

/// <summary>
/// Registers the in-process MCP tool host containing all five logical servers.
/// Servers are kept as scoped DI services so they can take a per-request
/// <see cref="FoiaProcessor.Data.FoiaDbContext"/> instance. The in-process
/// MCP transport is wired up by the API host (see Program.cs) so agents can
/// invoke tool methods through the Microsoft Agent Framework's MCP integration.
/// </summary>
public static class McpToolHostExtensions
{
    public static IServiceCollection AddFoiaMcpTools(this IServiceCollection services)
    {
        services.AddScoped<CaseServer>();
        services.AddScoped<SearchServer>();
        services.AddScoped<RedactionServer>();
        services.AddScoped<ReviewServer>();
        services.AddScoped<BlobStorageServer>();
        return services;
    }
}
