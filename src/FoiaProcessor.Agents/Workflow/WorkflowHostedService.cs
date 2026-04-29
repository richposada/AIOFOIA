using FoiaProcessor.Data;
using FoiaProcessor.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FoiaProcessor.Agents.Workflow;

/// <summary>
/// Background service: reads request ids from <see cref="WorkflowQueue"/>
/// and invokes <see cref="FoiaWorkflowRunner.RunAsync"/> for each one.
/// </summary>
public class WorkflowHostedService : BackgroundService
{
    private readonly WorkflowQueue _queue;
    private readonly FoiaWorkflowRunner _runner;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WorkflowHostedService> _logger;

    public WorkflowHostedService(
        WorkflowQueue queue,
        FoiaWorkflowRunner runner,
        IServiceScopeFactory scopeFactory,
        ILogger<WorkflowHostedService> logger)
    {
        _queue = queue;
        _runner = runner;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Workflow hosted service started.");

        // Recover any in-flight requests left behind by a previous process.
        // The in-memory WorkflowQueue does not survive restarts, so anything
        // already past Submitted but not yet at a terminal/pause state must be
        // re-enqueued or it will sit forever (e.g. ApprovedForRelease /
        // Packaging never advancing to ReleasePackageReady).
        await EnqueuePendingRequestsAsync(stoppingToken);

        await foreach (var requestId in _queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await _runner.RunAsync(requestId, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error processing FOIA request {RequestId}.", requestId);
            }
        }
        _logger.LogInformation("Workflow hosted service stopping.");
    }

    private async Task EnqueuePendingRequestsAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<FoiaDbContext>();

            // Only recover the post-human-review stages. The API enqueues
            // once on submission (covering Submitted → PendingHumanReview)
            // and once on release approval (covering ApprovedForRelease →
            // ReleasePackageReady). If the host crashes after release
            // approval but before Packaging completes, the in-memory queue
            // loses the work item and nothing else will ever pick it up —
            // hence the targeted recovery here.
            //
            // Upstream stages are intentionally NOT recovered: doing so
            // causes duplicate processing of in-flight requests and head-of
            // -line blocking on the SingleReader channel if any prior run
            // is hung.
            var resumable = new[]
            {
                RequestStatus.ApprovedForRelease,
                RequestStatus.Packaging,
            };

            var ids = await db.FoiaRequests
                .AsNoTracking()
                .Where(r => resumable.Contains(r.Status))
                .Select(r => r.Id)
                .ToListAsync(ct);

            foreach (var id in ids)
            {
                await _queue.EnqueueAsync(id, ct);
            }

            if (ids.Count > 0)
            {
                _logger.LogInformation(
                    "Workflow recovery: re-enqueued {Count} request(s) stuck in release packaging.", ids.Count);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown during startup; nothing to do.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Workflow recovery failed; in-flight requests may not resume until next restart.");
        }
    }
}
