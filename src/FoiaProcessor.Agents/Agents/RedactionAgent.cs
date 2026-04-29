using FoiaProcessor.Agents.Infrastructure;
using FoiaProcessor.Data;
using FoiaProcessor.Data.Entities;
using FoiaProcessor.McpTools.Contracts;
using FoiaProcessor.McpTools.Servers;
using Microsoft.EntityFrameworkCore;

namespace FoiaProcessor.Agents.Agents;

/// <summary>
/// Agent 3: For each pending document, runs deterministic PII detection and
/// redaction via the MCP-shaped servers. No LLM orchestration is required
/// because the steps (detect → redact → save) have no decision points.
/// </summary>
public class RedactionAgent
{
    private readonly CaseServer _case;
    private readonly RedactionServer _redaction;
    private readonly FoiaDbContext _db;
    private readonly AgentChatClientFactory _chatFactory;

    public RedactionAgent(
        CaseServer caseServer,
        RedactionServer redactionServer,
        FoiaDbContext db,
        AgentChatClientFactory chatFactory)
    {
        _case = caseServer;
        _redaction = redactionServer;
        _db = db;
        _chatFactory = chatFactory; // retained for DI compatibility; not used here
    }

    public virtual async Task RunAsync(Guid requestId, CancellationToken ct = default)
    {
        await _case.CaseUpdateStatusAsync(
            new CaseUpdateStatusInput(requestId, nameof(RequestStatus.Redacting), "Redaction starting."),
            ct);

        var docs = await _db.Documents
            .AsNoTracking()
            .Where(d => d.FoiaRequestId == requestId && d.RedactionStatus == RedactionStatus.NotStarted)
            .Select(d => new { d.Id, d.OriginalContent })
            .ToListAsync(ct);

        foreach (var d in docs)
        {
            if (string.IsNullOrEmpty(d.OriginalContent))
            {
                // Nothing to redact for this row; skip without failing the entire request.
                continue;
            }

            await RunForDocumentAsync(d.Id, d.OriginalContent, ct);

            // Reset the change tracker between documents so per-document state
            // (including any tracked Redaction children) does not bleed into
            // subsequent SaveChanges calls and trigger spurious concurrency
            // conflicts when the agent is resumed.
            _db.ChangeTracker.Clear();
        }

        await _case.CaseUpdateStatusAsync(
            new CaseUpdateStatusInput(requestId, nameof(RequestStatus.PendingHumanReview), "Awaiting human review."),
            ct);
    }

    private async Task RunForDocumentAsync(Guid documentId, string content, CancellationToken ct)
    {
        // Detect PII (regex + optional AI inside RedactionServer).
        var detected = await _redaction.DetectPiiAsync(new DetectPiiInput(content, UseAi: false), ct);

        // Apply redactions.
        var redacted = await _redaction.RedactTextAsync(
            new RedactTextInput(content, detected.Findings.ToList()), ct);

        // Persist redacted document + findings + audit.
        var findings = redacted.AppliedFindings
            .Select(a => new RedactionFinding(
                a.PiiType, a.OriginalText, a.ReplacementText,
                a.StartOffset, a.EndOffset, null, a.Confidence, a.DetectionSource))
            .ToList();

        await _case.CaseSaveRedactionResultsAsync(
            new CaseSaveRedactionResultsInput(documentId, redacted.RedactedText, findings), ct);
    }
}
