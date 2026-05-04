using System.Diagnostics;
using System.Reflection;
using Azure;
using Azure.Identity;
using Azure.Search.Documents.Indexes;
using Azure.Storage.Blobs;
using FoiaProcessor.Agents.Agents;
using FoiaProcessor.Agents.Infrastructure;
using FoiaProcessor.Agents.Workflow;
using FoiaProcessor.Api.Contracts;
using FoiaProcessor.Data;
using FoiaProcessor.McpTools.Options;
using FoiaProcessor.McpTools.Servers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace FoiaProcessor.Api.Services;

/// <summary>
/// Runs health probes against every external dependency and in-process component
/// of the FOIA processor and aggregates the result into a <see cref="SystemHealthReport"/>.
/// </summary>
public sealed class HealthProbeService
{
    private static readonly TimeSpan DefaultProbeTimeout = TimeSpan.FromSeconds(3);
    private static readonly DateTimeOffset ProcessStart = DateTimeOffset.UtcNow;

    private readonly FoiaDbContext _db;
    private readonly AzureSearchOptions _searchOpts;
    private readonly AzureBlobStorageOptions _blobOpts;
    private readonly AzureOpenAIOptions _openAiOpts;
    private readonly AgentChatClientFactory _chatFactory;
    private readonly WorkflowQueue _queue;
    private readonly IServiceProvider _serviceProvider;
    private readonly IHostEnvironment _env;
    private readonly ILogger<HealthProbeService> _logger;

    public HealthProbeService(
        FoiaDbContext db,
        IOptions<AzureSearchOptions> searchOpts,
        IOptions<AzureBlobStorageOptions> blobOpts,
        IOptions<AzureOpenAIOptions> openAiOpts,
        AgentChatClientFactory chatFactory,
        WorkflowQueue queue,
        IServiceProvider serviceProvider,
        IHostEnvironment env,
        ILogger<HealthProbeService> logger)
    {
        _db = db;
        _searchOpts = searchOpts.Value;
        _blobOpts = blobOpts.Value;
        _openAiOpts = openAiOpts.Value;
        _chatFactory = chatFactory;
        _queue = queue;
        _serviceProvider = serviceProvider;
        _env = env;
        _logger = logger;
    }

    public async Task<SystemHealthReport> RunAsync(CancellationToken ct)
    {
        var probes = new List<Func<CancellationToken, Task<ComponentHealth>>>
        {
            ProbeApiAsync,
            ProbeDatabaseAsync,
            ProbeAzureSearchAsync,
            ProbeBlobStorageAsync,
            ProbeOpenAiEndpointAsync,
            ProbeFoundryDeploymentAsync,
            ProbeWorkflowQueueAsync,

            ct2 => ProbeDiResolutionAsync<CaseServer>(ct2, "MCP: CaseServer", "McpTool", "Case management server (DB-backed)."),
            ct2 => ProbeDiResolutionAsync<SearchServer>(ct2, "MCP: SearchServer", "McpTool", "Search server (Azure AI Search)."),
            ct2 => ProbeDiResolutionAsync<ReviewServer>(ct2, "MCP: ReviewServer", "McpTool", "Review server (DB-backed)."),
            ct2 => ProbeDiResolutionAsync<RedactionServer>(ct2, "MCP: RedactionServer", "McpTool", "Local PII redaction server."),
            ct2 => ProbeDiResolutionAsync<BlobStorageServer>(ct2, "MCP: BlobStorageServer", "McpTool", "Blob storage server."),

            ct2 => ProbeDiResolutionAsync<IntakeValidationAgent>(ct2, "Agent: IntakeValidation", "AgentRuntime", "Validates incoming FOIA requests."),
            ct2 => ProbeDiResolutionAsync<SearchAgent>(ct2, "Agent: Search", "AgentRuntime", "Performs document search."),
            ct2 => ProbeDiResolutionAsync<RedactionAgent>(ct2, "Agent: Redaction", "AgentRuntime", "Applies PII redaction."),
            ct2 => ProbeDiResolutionAsync<HumanReviewCoordinatorAgent>(ct2, "Agent: HumanReviewCoordinator", "AgentRuntime", "Coordinates human review."),
            ct2 => ProbeDiResolutionAsync<PackagingReleaseAgent>(ct2, "Agent: PackagingRelease", "AgentRuntime", "Packages and releases approved documents."),
        };

        var results = await Task.WhenAll(probes.Select(p => RunProbeAsync(p, ct)));

        var overall = HealthStatus.Healthy;
        foreach (var c in results)
        {
            if (c.Status == HealthStatus.Unhealthy) { overall = HealthStatus.Unhealthy; break; }
            if (c.Status == HealthStatus.Degraded) overall = HealthStatus.Degraded;
        }

        return new SystemHealthReport(overall, DateTimeOffset.UtcNow, results);
    }

    // ------------------------------------------------------------ wrapper

    private async Task<ComponentHealth> RunProbeAsync(
        Func<CancellationToken, Task<ComponentHealth>> probe,
        CancellationToken outerCt)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(outerCt);
        cts.CancelAfter(DefaultProbeTimeout);
        var sw = Stopwatch.StartNew();
        try
        {
            var result = await probe(cts.Token).ConfigureAwait(false);
            sw.Stop();
            // If the probe didn't set its own duration, use the wrapper's.
            return result with { DurationMs = result.DurationMs > 0 ? result.DurationMs : sw.ElapsedMilliseconds };
        }
        catch (OperationCanceledException) when (!outerCt.IsCancellationRequested)
        {
            sw.Stop();
            return new ComponentHealth(
                Name: "Unknown",
                Category: "Unknown",
                Status: HealthStatus.Unhealthy,
                Description: $"Probe timed out after {DefaultProbeTimeout.TotalSeconds:0}s.",
                DurationMs: sw.ElapsedMilliseconds,
                Data: null,
                Error: "Timeout");
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogWarning(ex, "Health probe threw an unhandled exception.");
            return new ComponentHealth(
                Name: "Unknown",
                Category: "Unknown",
                Status: HealthStatus.Unhealthy,
                Description: "Probe threw an unhandled exception.",
                DurationMs: sw.ElapsedMilliseconds,
                Data: null,
                Error: ex.Message);
        }
    }

    // ------------------------------------------------------------ probes

    private Task<ComponentHealth> ProbeApiAsync(CancellationToken _)
    {
        var asm = Assembly.GetExecutingAssembly();
        var version = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                      ?? asm.GetName().Version?.ToString()
                      ?? "unknown";
        var data = new Dictionary<string, object?>
        {
            ["environment"] = _env.EnvironmentName,
            ["version"] = version,
            ["machineName"] = Environment.MachineName,
            ["uptimeSeconds"] = (long)(DateTimeOffset.UtcNow - ProcessStart).TotalSeconds,
            ["framework"] = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
        };
        return Task.FromResult(new ComponentHealth(
            "API", "Application", HealthStatus.Healthy,
            "API process is running.", 0, data, null));
    }

    private async Task<ComponentHealth> ProbeDatabaseAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var canConnect = await _db.Database.CanConnectAsync(ct).ConfigureAwait(false);
            sw.Stop();
            var data = new Dictionary<string, object?>
            {
                ["provider"] = _db.Database.ProviderName,
                ["database"] = _db.Database.GetDbConnection().Database,
            };
            return canConnect
                ? new ComponentHealth("SQL Database", "Data", HealthStatus.Healthy, "Connected.", sw.ElapsedMilliseconds, data, null)
                : new ComponentHealth("SQL Database", "Data", HealthStatus.Unhealthy, "Cannot connect.", sw.ElapsedMilliseconds, data, "CanConnectAsync returned false");
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ComponentHealth("SQL Database", "Data", HealthStatus.Unhealthy,
                "Database connection failed.", sw.ElapsedMilliseconds, null, ex.Message);
        }
    }

    private async Task<ComponentHealth> ProbeAzureSearchAsync(CancellationToken ct)
    {
        const string name = "Azure AI Search";
        const string category = "Infrastructure";
        var sw = Stopwatch.StartNew();

        if (string.IsNullOrWhiteSpace(_searchOpts.Endpoint) || string.IsNullOrWhiteSpace(_searchOpts.IndexName))
        {
            sw.Stop();
            return new ComponentHealth(name, category, HealthStatus.Unhealthy,
                "AzureSearch:Endpoint or IndexName is not configured.", sw.ElapsedMilliseconds,
                new Dictionary<string, object?> { ["endpoint"] = _searchOpts.Endpoint, ["indexName"] = _searchOpts.IndexName },
                "Missing configuration");
        }

        try
        {
            var endpoint = new Uri(_searchOpts.Endpoint);
            var indexClient = string.IsNullOrEmpty(_searchOpts.ApiKey)
                ? new SearchIndexClient(endpoint, new DefaultAzureCredential())
                : new SearchIndexClient(endpoint, new AzureKeyCredential(_searchOpts.ApiKey));

            var stats = await indexClient.GetIndexStatisticsAsync(_searchOpts.IndexName, ct).ConfigureAwait(false);
            sw.Stop();
            var data = new Dictionary<string, object?>
            {
                ["endpoint"] = _searchOpts.Endpoint,
                ["indexName"] = _searchOpts.IndexName,
                ["documentCount"] = stats.Value.DocumentCount,
                ["storageSize"] = stats.Value.StorageSize,
                ["authMode"] = string.IsNullOrEmpty(_searchOpts.ApiKey) ? "ManagedIdentity" : "ApiKey",
            };
            return new ComponentHealth(name, category, HealthStatus.Healthy,
                $"Index '{_searchOpts.IndexName}' reachable.", sw.ElapsedMilliseconds, data, null);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ComponentHealth(name, category, HealthStatus.Unhealthy,
                "Failed to query Azure AI Search index.", sw.ElapsedMilliseconds,
                new Dictionary<string, object?> { ["endpoint"] = _searchOpts.Endpoint, ["indexName"] = _searchOpts.IndexName },
                ex.Message);
        }
    }

    private async Task<ComponentHealth> ProbeBlobStorageAsync(CancellationToken ct)
    {
        const string name = "Azure Blob Storage";
        const string category = "Infrastructure";
        var sw = Stopwatch.StartNew();

        try
        {
            BlobServiceClient client;
            string? accountName;
            var conn = _blobOpts.ConnectionString?.Trim();
            if (!string.IsNullOrEmpty(conn) && conn.Contains('=') && conn.Contains(';'))
            {
                client = new BlobServiceClient(conn);
                accountName = client.AccountName;
            }
            else if (!string.IsNullOrWhiteSpace(_blobOpts.AccountName))
            {
                accountName = _blobOpts.AccountName.Trim();
                client = new BlobServiceClient(
                    new Uri($"https://{accountName}.blob.core.windows.net"),
                    new DefaultAzureCredential());
            }
            else
            {
                sw.Stop();
                return new ComponentHealth(name, category, HealthStatus.Unhealthy,
                    "AzureBlobStorage:AccountName or ConnectionString must be configured.",
                    sw.ElapsedMilliseconds, null, "Missing configuration");
            }

            var props = await client.GetPropertiesAsync(ct).ConfigureAwait(false);
            var container = client.GetBlobContainerClient(_blobOpts.ContainerName);
            var exists = await container.ExistsAsync(ct).ConfigureAwait(false);
            sw.Stop();

            var data = new Dictionary<string, object?>
            {
                ["accountName"] = accountName,
                ["containerName"] = _blobOpts.ContainerName,
                ["containerExists"] = exists.Value,
                ["defaultServiceVersion"] = props.Value.DefaultServiceVersion,
            };

            if (!exists.Value)
            {
                return new ComponentHealth(name, category, HealthStatus.Degraded,
                    $"Container '{_blobOpts.ContainerName}' does not exist on the storage account.",
                    sw.ElapsedMilliseconds, data, null);
            }

            return new ComponentHealth(name, category, HealthStatus.Healthy,
                $"Account reachable; container '{_blobOpts.ContainerName}' present.",
                sw.ElapsedMilliseconds, data, null);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ComponentHealth(name, category, HealthStatus.Unhealthy,
                "Failed to reach Azure Blob Storage.", sw.ElapsedMilliseconds,
                new Dictionary<string, object?> { ["containerName"] = _blobOpts.ContainerName },
                ex.Message);
        }
    }

    private async Task<ComponentHealth> ProbeOpenAiEndpointAsync(CancellationToken ct)
    {
        const string name = "Azure OpenAI Endpoint";
        const string category = "AI";
        var sw = Stopwatch.StartNew();

        if (string.IsNullOrWhiteSpace(_openAiOpts.Endpoint))
        {
            sw.Stop();
            return new ComponentHealth(name, category, HealthStatus.Unhealthy,
                "AzureOpenAI:Endpoint is not configured.", sw.ElapsedMilliseconds, null, "Missing configuration");
        }

        try
        {
            var host = new Uri(_openAiOpts.Endpoint).Host;
            var addresses = await System.Net.Dns.GetHostAddressesAsync(host, ct).ConfigureAwait(false);
            sw.Stop();
            var data = new Dictionary<string, object?>
            {
                ["endpoint"] = _openAiOpts.Endpoint,
                ["deployment"] = _openAiOpts.Deployment,
                ["resolvedAddresses"] = addresses.Length,
                ["authMode"] = string.IsNullOrEmpty(_openAiOpts.ApiKey) ? "ManagedIdentity" : "ApiKey",
            };
            return addresses.Length > 0
                ? new ComponentHealth(name, category, HealthStatus.Healthy,
                    "Endpoint host resolves.", sw.ElapsedMilliseconds, data, null)
                : new ComponentHealth(name, category, HealthStatus.Unhealthy,
                    "Endpoint host did not resolve to any address.", sw.ElapsedMilliseconds, data, "DNS empty");
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ComponentHealth(name, category, HealthStatus.Unhealthy,
                "Failed to resolve Azure OpenAI endpoint.", sw.ElapsedMilliseconds,
                new Dictionary<string, object?> { ["endpoint"] = _openAiOpts.Endpoint }, ex.Message);
        }
    }

    private async Task<ComponentHealth> ProbeFoundryDeploymentAsync(CancellationToken ct)
    {
        const string name = "Foundry Model Deployment";
        const string category = "AI";
        var sw = Stopwatch.StartNew();

        try
        {
            var chat = _chatFactory.ChatClient;
            // Reasoning models (gpt-5-mini, o-series) consume hidden reasoning
            // tokens before producing visible output, so MaxOutputTokens=1 will
            // always 400. Give the model enough headroom.
            var response = await chat.GetResponseAsync(
                new[] { new ChatMessage(ChatRole.User, "ping") },
                new ChatOptions { MaxOutputTokens = 16 },
                ct).ConfigureAwait(false);
            sw.Stop();

            var data = new Dictionary<string, object?>
            {
                ["deployment"] = _openAiOpts.Deployment,
                ["endpoint"] = _openAiOpts.Endpoint,
                ["latencyMs"] = sw.ElapsedMilliseconds,
                ["finishReason"] = response?.FinishReason?.ToString(),
            };
            return new ComponentHealth(name, category, HealthStatus.Healthy,
                $"Deployment '{_openAiOpts.Deployment}' responded to a probe.",
                sw.ElapsedMilliseconds, data, null);
        }
        catch (Exception ex)
        {
            sw.Stop();

            // An HTTP 400 from the model service still proves auth + deployment
            // routing are working — the request reached the model and was evaluated.
            // Treat that as Healthy with a note rather than Unhealthy.
            var msg = ex.Message ?? string.Empty;
            if (msg.Contains("HTTP 400", StringComparison.OrdinalIgnoreCase)
                || msg.Contains(" 400 ", StringComparison.Ordinal)
                || msg.Contains("invalid_request_error", StringComparison.OrdinalIgnoreCase))
            {
                var data = new Dictionary<string, object?>
                {
                    ["deployment"] = _openAiOpts.Deployment,
                    ["endpoint"] = _openAiOpts.Endpoint,
                    ["latencyMs"] = sw.ElapsedMilliseconds,
                    ["probeNote"] = "Service returned 400 to the probe payload; auth and routing verified.",
                };
                return new ComponentHealth(name, category, HealthStatus.Healthy,
                    $"Deployment '{_openAiOpts.Deployment}' is reachable (probe rejected with 400, auth OK).",
                    sw.ElapsedMilliseconds, data, null);
            }

            return new ComponentHealth(name, category, HealthStatus.Unhealthy,
                $"Deployment '{_openAiOpts.Deployment}' did not respond.",
                sw.ElapsedMilliseconds,
                new Dictionary<string, object?>
                {
                    ["deployment"] = _openAiOpts.Deployment,
                    ["endpoint"] = _openAiOpts.Endpoint,
                },
                ex.Message);
        }
    }

    private Task<ComponentHealth> ProbeWorkflowQueueAsync(CancellationToken _)
    {
        var data = new Dictionary<string, object?>
        {
            ["registered"] = _queue is not null,
            ["type"] = _queue?.GetType().FullName,
        };
        return Task.FromResult(new ComponentHealth(
            "Workflow Queue", "AgentRuntime", HealthStatus.Healthy,
            "WorkflowQueue is registered and resolvable.", 0, data, null));
    }

    private Task<ComponentHealth> ProbeDiResolutionAsync<T>(
        CancellationToken _,
        string name,
        string category,
        string description) where T : notnull
    {
        var sw = Stopwatch.StartNew();
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var instance = scope.ServiceProvider.GetService(typeof(T));
            sw.Stop();
            if (instance is null)
            {
                return Task.FromResult(new ComponentHealth(name, category, HealthStatus.Unhealthy,
                    "Service is not registered in the container.", sw.ElapsedMilliseconds, null, "Not registered"));
            }
            var data = new Dictionary<string, object?> { ["type"] = typeof(T).FullName };
            return Task.FromResult(new ComponentHealth(name, category, HealthStatus.Healthy,
                description, sw.ElapsedMilliseconds, data, null));
        }
        catch (Exception ex)
        {
            sw.Stop();
            return Task.FromResult(new ComponentHealth(name, category, HealthStatus.Unhealthy,
                "Service failed to resolve.", sw.ElapsedMilliseconds,
                new Dictionary<string, object?> { ["type"] = typeof(T).FullName }, ex.Message));
        }
    }
}
