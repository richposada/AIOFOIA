using FoiaProcessor.Agents.Agents;
using FoiaProcessor.Data;
using FoiaProcessor.Data.Audit;
using FoiaProcessor.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FoiaProcessor.Agents.Workflow;

/// <summary>
/// Orchestrates the five FOIA agents in order. Pauses at human review by exiting
/// after the coordinator agent runs; resumes from the persisted DB state when
/// the API enqueues the request again after release approval.
/// </summary>
public class FoiaWorkflowRunner
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<FoiaWorkflowRunner> _logger;

    public FoiaWorkflowRunner(IServiceScopeFactory scopeFactory, ILogger<FoiaWorkflowRunner> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task RunAsync(Guid requestId, CancellationToken ct = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<FoiaDbContext>();
        var audit = sp.GetRequiredService<IAuditWriter>();

        var request = await db.FoiaRequests.FirstOrDefaultAsync(r => r.Id == requestId, ct);
        if (request is null)
        {
            _logger.LogWarning("Workflow runner: FOIA request {RequestId} not found.", requestId);
            return;
        }

        try
        {
            // Resume semantics: pick up from the persisted Status.
            switch (request.Status)
            {
                case RequestStatus.Submitted:
                    await sp.GetRequiredService<IntakeValidationAgent>().RunAsync(requestId, ct);
                    goto case RequestStatus.Validated;

                case RequestStatus.Validated:
                case RequestStatus.Searching:
                    await sp.GetRequiredService<SearchAgent>().RunAsync(requestId, ct);
                    await db.Entry(request).ReloadAsync(ct);
                    if (request.Status == RequestStatus.NoDocumentsFound) return;
                    goto case RequestStatus.DocumentsFound;

                case RequestStatus.DocumentsFound:
                case RequestStatus.Redacting:
                    await sp.GetRequiredService<RedactionAgent>().RunAsync(requestId, ct);
                    goto case RequestStatus.PendingHumanReview;

                case RequestStatus.PendingHumanReview:
                    await sp.GetRequiredService<HumanReviewCoordinatorAgent>().RunAsync(requestId, ct);
                    // Pause here — workflow will be re-enqueued on release approval.
                    return;

                case RequestStatus.ApprovedForRelease:
                case RequestStatus.Packaging:
                    await sp.GetRequiredService<PackagingReleaseAgent>().RunAsync(requestId, ct);
                    return;

                default:
                    _logger.LogInformation("Workflow runner: nothing to do for request {RequestId} in status {Status}.", requestId, request.Status);
                    return;
            }
        }
        catch (Exception ex)
        {
            if (ex is DbUpdateConcurrencyException cex)
            {
                foreach (var entry in cex.Entries)
                {
                    var pk = string.Join(",", entry.Properties
                        .Where(p => p.Metadata.IsPrimaryKey())
                        .Select(p => $"{p.Metadata.Name}={p.CurrentValue}"));
                    var modified = string.Join(",", entry.Properties
                        .Where(p => p.IsModified)
                        .Select(p => p.Metadata.Name));
                    _logger.LogError(
                        "Concurrency conflict on {EntityType} ({Pk}, State={State}, Modified=[{Modified}]) for request {RequestId}.",
                        entry.Entity.GetType().Name, pk, entry.State, modified, requestId);
                }
            }

            _logger.LogError(ex, "Workflow failed for request {RequestId}.", requestId);
            try
            {
                // Discard any partially-applied changes from the failed agent run so they
                // don't get re-flushed (and trigger spurious concurrency failures) when we
                // persist the Error status below.
                db.ChangeTracker.Clear();

                var fresh = await db.FoiaRequests.FirstOrDefaultAsync(r => r.Id == requestId, ct);
                if (fresh is not null)
                {
                    fresh.Status = RequestStatus.Error;
                    await db.SaveChangesAsync(ct);
                }

                await audit.RecordAsync(requestId, AuditEventType.Error, $"Workflow error: {ex.Message}", ct: ct);
            }
            catch (Exception inner)
            {
                _logger.LogError(inner, "Failed to persist error state for request {RequestId}.", requestId);
            }
        }
    }
}
