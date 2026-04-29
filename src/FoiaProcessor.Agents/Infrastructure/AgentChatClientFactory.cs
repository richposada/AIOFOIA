using System.ClientModel;
using Azure.AI.OpenAI;
using Azure.Identity;
using FoiaProcessor.McpTools.Options;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace FoiaProcessor.Agents.Infrastructure;

/// <summary>
/// Builds a singleton <see cref="IChatClient"/> backed by Azure OpenAI for use
/// by all <c>ChatClientAgent</c> instances in the workflow.
/// </summary>
public class AgentChatClientFactory
{
    private readonly AzureOpenAIOptions _opts;
    private readonly Lazy<IChatClient> _client;

    public AgentChatClientFactory(IOptions<AzureOpenAIOptions> opts)
    {
        _opts = opts.Value;
        _client = new Lazy<IChatClient>(Build);
    }

    public IChatClient ChatClient => _client.Value;

    private IChatClient Build()
    {
        if (string.IsNullOrWhiteSpace(_opts.Endpoint))
        {
            throw new InvalidOperationException(
                "AzureOpenAI:Endpoint is not configured. Required for Microsoft Agent Framework agents.");
        }

        var endpoint = new Uri(_opts.Endpoint);
        var azureClient = string.IsNullOrWhiteSpace(_opts.ApiKey)
            ? new AzureOpenAIClient(endpoint, new DefaultAzureCredential())
            : new AzureOpenAIClient(endpoint, new ApiKeyCredential(_opts.ApiKey));

        return azureClient
            .GetChatClient(_opts.Deployment)
            .AsIChatClient();
    }
}
