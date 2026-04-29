using FoiaProcessor.Agents.Agents;
using FoiaProcessor.Agents.Infrastructure;
using FoiaProcessor.Agents.Workflow;
using Microsoft.Extensions.DependencyInjection;

namespace FoiaProcessor.Agents;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddFoiaAgents(this IServiceCollection services)
    {
        services.AddSingleton<AgentChatClientFactory>();
        services.AddScoped<IntakeValidationAgent>();
        services.AddScoped<SearchAgent>();
        services.AddScoped<RedactionAgent>();
        services.AddScoped<HumanReviewCoordinatorAgent>();
        services.AddScoped<PackagingReleaseAgent>();

        services.AddSingleton<WorkflowQueue>();
        services.AddSingleton<FoiaWorkflowRunner>();
        services.AddHostedService<WorkflowHostedService>();
        return services;
    }
}
