using FoiaProcessor.Agents.Agents;
using FoiaProcessor.Data;
using FoiaProcessor.Data.Audit;
using FoiaProcessor.Data.Entities;
using Microsoft.Agents.AI.Workflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FoiaProcessor.Agents.Workflow;

/// <summary>
/// Orchestrates the five FOIA agents using a Microsoft Agent Framework (MAF)
/// <see cref="Workflow"/>. The pipeline is expressed as a directed graph of
/// <see cref="Executor"/> nodes connected by typed edges with conditional
/// routing, instead of a hand-rolled switch/goto state machine.
///
/// What MAF gives us here:
///  - Type-safe message passing (<see cref="FoiaWorkflowMessage"/>) between stages.
///  - Declarative routing: a switch on the persisted <see cref="RequestStatus"/>
///    at the start (resume semantics) and a conditional edge for the
///    "no documents found" short-circuit.
///  - Per-executor observability events (executor started/completed/failed)
///    emitted by the framework -- no manual logging at every stage.
///  - Deterministic superstep execution model (BSP) -- easy to reason about
///    and safe to checkpoint or extend with parallel fan-out later (e.g.
///    redact pages in parallel) without changing the orchestration code.
///  - Built-in workflow validation at Build() time (graph connectivity, type
///    compatibility between executors).
///
/// The existing agents are reused as-is -- each MAF executor is a thin adapter
/// that opens a DI scope, resolves the agent, and calls its existing
/// RunAsync(Guid, CancellationToken) method.
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

    /// <summary>
    /// Build and execute the FOIA workflow for a single request id. The caller
    /// (the hosted service consuming <see cref="WorkflowQueue"/>) invokes this
    /// once per enqueued request -- the workflow itself decides which stages to
    /// run based on the persisted <see cref="RequestStatus"/>.
    /// </summary>
    public async Task RunAsync(Guid requestId, CancellationToken ct = default)
    {
        // Verify the request exists before spinning up a workflow run.
        await using (var preScope = _scopeFactory.CreateAsyncScope())
        {
            var preDb = preScope.ServiceProvider.GetRequiredService<FoiaDbContext>();
            var exists = await preDb.FoiaRequests.AsNoTracking().AnyAsync(r => r.Id == requestId, ct);
            if (!exists)
            {
                _logger.LogWarning("Workflow runner: FOIA request {RequestId} not found.", requestId);
                return;
            }
        }

        try
        {
            // -----------------------------------------------------------------
            // 1) Build the executor graph.
            //
            // Each executor is a stateless adapter around one of the existing
            // agents. They share a service-scope factory so every handler runs
            // in its own DI scope (matching the previous behaviour).
            // -----------------------------------------------------------------
            var router    = new RouterExecutor(_scopeFactory);
            var intake    = new IntakeExecutor(_scopeFactory);
            var search    = new SearchExecutor(_scopeFactory);
            var redaction = new RedactionExecutor(_scopeFactory);
            var review    = new HumanReviewExecutor(_scopeFactory);
            var packaging = new PackagingExecutor(_scopeFactory);
            var noop      = new NoopExecutor();

            // -----------------------------------------------------------------
            // 2) Wire the graph with WorkflowBuilder.
            //
            // The router emits a FoiaWorkflowMessage tagged with the current
            // persisted status. A switch-case edge from the router resumes the
            // pipeline at the correct stage (replaces the previous goto-case
            // fall-through state machine).
            //
            // Linear edges between subsequent stages carry the message produced
            // by each executor. The Search -> Redaction edge is conditional: if
            // the search stage flipped the status to NoDocumentsFound, no edge
            // fires and the workflow naturally terminates.
            // -----------------------------------------------------------------
            var workflow = new WorkflowBuilder(router)
                .AddSwitch(router, sw => sw
                    // Submitted -> run intake validation first.
                    .AddCase(StatusIs(RequestStatus.Submitted), [intake])
                    // Resuming after an interrupted Validated/Searching state.
                    .AddCase(StatusIsAny(RequestStatus.Validated, RequestStatus.Searching), [search])
                    // Resuming a partially-completed redaction pass.
                    .AddCase(StatusIsAny(RequestStatus.DocumentsFound, RequestStatus.Redacting), [redaction])
                    // Re-entry into the human-review coordinator (idempotent).
                    .AddCase(StatusIs(RequestStatus.PendingHumanReview), [review])
                    // The reviewer approved release -- run packaging.
                    .AddCase(StatusIsAny(RequestStatus.ApprovedForRelease, RequestStatus.Packaging), [packaging])
                    // Anything else (terminal / unknown) -> no-op terminal node.
                    .WithDefault([noop]))

                // Linear pipeline after intake.
                .AddEdge(intake, search)

                // Conditional edge: only proceed to redaction if the search
                // stage actually found documents. NoDocumentsFound short-circuits
                // the run by leaving no outgoing edge that fires.
                .AddEdge<FoiaWorkflowMessage>(search, redaction,
                    condition: msg => msg is not null && msg.Status != RequestStatus.NoDocumentsFound)

                // Redaction always hands off to the human-review coordinator.
                // The review executor calls YieldOutputAsync to terminate the
                // run -- the workflow will be re-invoked from the queue once
                // the human approves release (re-entering via the router's
                // switch).
                .AddEdge(redaction, review)

                // Mark the terminal nodes whose output ends the workflow run.
                .WithOutputFrom(review, packaging, noop)
                .Build();

            // -----------------------------------------------------------------
            // 3) Execute the workflow in-process.
            //
            // We use the streaming form so we can surface workflow / executor
            // failure events in our logs. The router executor will re-read the
            // current status from the DB; the placeholder status passed here
            // is overwritten before any routing decisions are made.
            // -----------------------------------------------------------------
            await using var run = await InProcessExecution.RunStreamingAsync(
                workflow,
                new FoiaWorkflowMessage(requestId, RequestStatus.Submitted));

            await foreach (var evt in run.WatchStreamAsync().WithCancellation(ct))
            {
                switch (evt)
                {
                    case ExecutorCompletedEvent completed:
                        _logger.LogDebug("Workflow {RequestId}: executor {ExecutorId} completed.",
                            requestId, completed.ExecutorId);
                        break;

                    case ExecutorFailedEvent failed:
                        // Surface the failure as an exception so the outer
                        // try/catch can record the Error status.
                        throw failed.Data as Exception
                              ?? new InvalidOperationException(
                                  $"Executor '{failed.ExecutorId}' failed.");

                    case WorkflowErrorEvent err when err.Exception is not null:
                        throw err.Exception;
                }
            }
        }
        catch (Exception ex)
        {
            await HandleWorkflowFailureAsync(requestId, ex, ct);
        }
    }

    // -------- Edge condition helpers --------------------------------------

    /// <summary>Edge condition: message carries the given status.</summary>
    private static Func<object?, bool> StatusIs(RequestStatus expected) =>
        msg => msg is FoiaWorkflowMessage m && m.Status == expected;

    /// <summary>Edge condition: message carries any of the given statuses.</summary>
    private static Func<object?, bool> StatusIsAny(params RequestStatus[] expected) =>
        msg => msg is FoiaWorkflowMessage m && Array.IndexOf(expected, m.Status) >= 0;

    // -------- Failure handling (preserved from the previous implementation) --

    private async Task HandleWorkflowFailureAsync(Guid requestId, Exception ex, CancellationToken ct)
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

        // Persist the Error status in a fresh scope so we don't reuse a
        // DbContext whose change tracker may be polluted by the failed run.
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var sp = scope.ServiceProvider;
            var db = sp.GetRequiredService<FoiaDbContext>();
            var audit = sp.GetRequiredService<IAuditWriter>();

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

// =========================================================================
// Workflow message
// =========================================================================

/// <summary>
/// The single typed message that flows along edges in the FOIA workflow.
/// Carries the request id plus the most recently observed persisted status,
/// which edge conditions use for routing decisions.
/// </summary>
public sealed record FoiaWorkflowMessage(Guid RequestId, RequestStatus Status);

// =========================================================================
// Executors -- one per pipeline stage.
//
// Each executor derives from MAF's strongly-typed Executor<TIn, TOut> base
// (or Executor<TIn> for terminal nodes) and overrides HandleAsync. The base
// class wires up the message protocol via reflection on the generic
// arguments.
// =========================================================================

/// <summary>
/// Entry-point executor. Reads the persisted status from the database and
/// emits a <see cref="FoiaWorkflowMessage"/>; the switch-case edge from this
/// node selects the correct downstream stage to resume from.
/// </summary>
internal sealed class RouterExecutor : Executor<FoiaWorkflowMessage, FoiaWorkflowMessage>
{
    private readonly IServiceScopeFactory _scopeFactory;

    public RouterExecutor(IServiceScopeFactory scopeFactory) : base(nameof(RouterExecutor))
    {
        _scopeFactory = scopeFactory;
    }

    public override async ValueTask<FoiaWorkflowMessage> HandleAsync(
        FoiaWorkflowMessage message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        // Always re-read the authoritative status from the DB; the runner
        // passes a placeholder "Submitted" message that we overwrite here.
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoiaDbContext>();
        var status = await db.FoiaRequests
            .AsNoTracking()
            .Where(r => r.Id == message.RequestId)
            .Select(r => r.Status)
            .FirstAsync(cancellationToken);

        return new FoiaWorkflowMessage(message.RequestId, status);
    }
}

/// <summary>Wraps <see cref="IntakeValidationAgent"/>.</summary>
internal sealed class IntakeExecutor : Executor<FoiaWorkflowMessage, FoiaWorkflowMessage>
{
    private readonly IServiceScopeFactory _scopeFactory;

    public IntakeExecutor(IServiceScopeFactory scopeFactory) : base(nameof(IntakeExecutor))
    {
        _scopeFactory = scopeFactory;
    }

    public override ValueTask<FoiaWorkflowMessage> HandleAsync(
        FoiaWorkflowMessage message, IWorkflowContext context, CancellationToken cancellationToken = default)
        => FoiaExecutorHelpers.RunAgentAsync<IntakeValidationAgent>(
            _scopeFactory, message.RequestId, (a, id, c) => a.RunAsync(id, c), cancellationToken);
}

/// <summary>Wraps <see cref="SearchAgent"/>.</summary>
internal sealed class SearchExecutor : Executor<FoiaWorkflowMessage, FoiaWorkflowMessage>
{
    private readonly IServiceScopeFactory _scopeFactory;

    public SearchExecutor(IServiceScopeFactory scopeFactory) : base(nameof(SearchExecutor))
    {
        _scopeFactory = scopeFactory;
    }

    public override ValueTask<FoiaWorkflowMessage> HandleAsync(
        FoiaWorkflowMessage message, IWorkflowContext context, CancellationToken cancellationToken = default)
        => FoiaExecutorHelpers.RunAgentAsync<SearchAgent>(
            _scopeFactory, message.RequestId, (a, id, c) => a.RunAsync(id, c), cancellationToken);
}

/// <summary>Wraps <see cref="RedactionAgent"/>.</summary>
internal sealed class RedactionExecutor : Executor<FoiaWorkflowMessage, FoiaWorkflowMessage>
{
    private readonly IServiceScopeFactory _scopeFactory;

    public RedactionExecutor(IServiceScopeFactory scopeFactory) : base(nameof(RedactionExecutor))
    {
        _scopeFactory = scopeFactory;
    }

    public override ValueTask<FoiaWorkflowMessage> HandleAsync(
        FoiaWorkflowMessage message, IWorkflowContext context, CancellationToken cancellationToken = default)
        => FoiaExecutorHelpers.RunAgentAsync<RedactionAgent>(
            _scopeFactory, message.RequestId, (a, id, c) => a.RunAsync(id, c), cancellationToken);
}

/// <summary>
/// Wraps <see cref="HumanReviewCoordinatorAgent"/>. After running, this
/// executor yields its output (the post-coordinator message) which marks the
/// terminal node for this run -- the workflow pauses here until the API
/// re-enqueues the request after a reviewer approves release.
/// </summary>
[YieldsOutput(typeof(FoiaWorkflowMessage))]
internal sealed class HumanReviewExecutor : Executor<FoiaWorkflowMessage>
{
    private readonly IServiceScopeFactory _scopeFactory;

    public HumanReviewExecutor(IServiceScopeFactory scopeFactory) : base(nameof(HumanReviewExecutor))
    {
        _scopeFactory = scopeFactory;
    }

    public override async ValueTask HandleAsync(
        FoiaWorkflowMessage message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        var result = await FoiaExecutorHelpers.RunAgentAsync<HumanReviewCoordinatorAgent>(
            _scopeFactory, message.RequestId, (a, id, c) => a.RunAsync(id, c), cancellationToken);

        // YieldOutputAsync ends this workflow run; resume happens on a fresh
        // run when the request is re-enqueued post-approval.
        await context.YieldOutputAsync(result, cancellationToken);
    }
}

/// <summary>
/// Wraps <see cref="PackagingReleaseAgent"/>. Terminal node for the
/// approved -> released branch.
/// </summary>
[YieldsOutput(typeof(FoiaWorkflowMessage))]
internal sealed class PackagingExecutor : Executor<FoiaWorkflowMessage>
{
    private readonly IServiceScopeFactory _scopeFactory;

    public PackagingExecutor(IServiceScopeFactory scopeFactory) : base(nameof(PackagingExecutor))
    {
        _scopeFactory = scopeFactory;
    }

    public override async ValueTask HandleAsync(
        FoiaWorkflowMessage message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        var result = await FoiaExecutorHelpers.RunAgentAsync<PackagingReleaseAgent>(
            _scopeFactory, message.RequestId, (a, id, c) => a.RunAsync(id, c), cancellationToken);
        await context.YieldOutputAsync(result, cancellationToken);
    }
}

/// <summary>
/// No-op terminal executor used as the default branch of the router switch
/// for terminal/unknown statuses (e.g. <see cref="RequestStatus.ReleasePackageReady"/>,
/// <see cref="RequestStatus.NoDocumentsFound"/>, <see cref="RequestStatus.Error"/>).
/// </summary>
[YieldsOutput(typeof(FoiaWorkflowMessage))]
internal sealed class NoopExecutor : Executor<FoiaWorkflowMessage>
{
    public NoopExecutor() : base(nameof(NoopExecutor)) { }

    public override async ValueTask HandleAsync(
        FoiaWorkflowMessage message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        await context.YieldOutputAsync(message, cancellationToken);
    }
}

// =========================================================================
// Shared executor helper
// =========================================================================

internal static class FoiaExecutorHelpers
{
    /// <summary>
    /// Common scaffolding for every agent executor: open a DI scope, resolve
    /// the agent, run it, then reload the persisted status (which the agent
    /// may have advanced) and emit the next workflow message.
    /// </summary>
    public static async ValueTask<FoiaWorkflowMessage> RunAgentAsync<TAgent>(
        IServiceScopeFactory scopeFactory,
        Guid requestId,
        Func<TAgent, Guid, CancellationToken, Task> invoke,
        CancellationToken ct)
        where TAgent : notnull
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var agent = sp.GetRequiredService<TAgent>();
        await invoke(agent, requestId, ct);

        // Read the freshly-persisted status so downstream edge conditions can
        // make accurate routing decisions.
        var db = sp.GetRequiredService<FoiaDbContext>();
        var status = await db.FoiaRequests
            .AsNoTracking()
            .Where(r => r.Id == requestId)
            .Select(r => r.Status)
            .FirstAsync(ct);

        return new FoiaWorkflowMessage(requestId, status);
    }
}
