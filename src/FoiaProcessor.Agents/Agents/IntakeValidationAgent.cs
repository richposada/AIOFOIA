using System.ComponentModel;
using System.Text.Json;
using FoiaProcessor.Agents.Infrastructure;
using FoiaProcessor.Data;
using FoiaProcessor.Data.Entities;
using FoiaProcessor.McpTools.Contracts;
using FoiaProcessor.McpTools.Servers;
using Microsoft.Agents.AI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;

namespace FoiaProcessor.Agents.Agents;

/// <summary>
/// Agent 1 (Microsoft Agent Framework): The LLM inspects the request payload and
/// decides whether to call <c>set_validated</c> or <c>set_rejected</c>. Both tools
/// route through the <see cref="CaseServer"/> MCP-shaped server.
/// </summary>
public class IntakeValidationAgent
{
    private readonly CaseServer _case;
    private readonly FoiaDbContext _db;
    private readonly AgentChatClientFactory _chatFactory;

    public IntakeValidationAgent(CaseServer caseServer, FoiaDbContext db, AgentChatClientFactory chatFactory)
    {
        _case = caseServer;
        _db = db;
        _chatFactory = chatFactory;
    }

    public virtual async Task RunAsync(Guid requestId, CancellationToken ct = default)
    {
        var request = await _db.FoiaRequests.AsNoTracking().FirstOrDefaultAsync(r => r.Id == requestId, ct)
            ?? throw new KeyNotFoundException($"FoiaRequest {requestId} not found.");

        [Description("Mark the FOIA request as validated. Call only when all required fields are present and dates are coherent.")]
        async Task<string> SetValidated([Description("Short note explaining why validation passed.")] string note)
        {
            await _case.CaseUpdateStatusAsync(
                new CaseUpdateStatusInput(requestId, nameof(RequestStatus.Validated), note),
                ct);
            return "validated";
        }

        [Description("Mark the FOIA request as rejected because validation failed.")]
        async Task<string> SetRejected([Description("Reason explaining what is missing or invalid.")] string reason)
        {
            await _case.CaseUpdateStatusAsync(
                new CaseUpdateStatusInput(requestId, nameof(RequestStatus.Rejected), $"Validation failed: {reason}"),
                ct);
            return "rejected";
        }

        var agent = new ChatClientAgent(
            _chatFactory.ChatClient,
            instructions: """
            You are the Intake Validation agent for a FOIA processing pipeline.
            Inspect the supplied JSON request and decide whether it is valid.
            Required fields: Subject, RequestorFullName, RequestorEmail (must look like an email),
            RequestedStartDate, RequestedEndDate. RequestedEndDate must be on or after RequestedStartDate.
            If everything is present and consistent, call SetValidated with a brief note.
            Otherwise, call SetRejected with a clear reason listing every problem.
            Call exactly one tool, then stop.
            """,
            name: "IntakeValidator",
            description: null,
            tools: new List<AITool>
            {
                AIFunctionFactory.Create((Delegate)SetValidated),
                AIFunctionFactory.Create((Delegate)SetRejected),
            });

        var payload = JsonSerializer.Serialize(new
        {
            request.Subject,
            request.Description,
            request.RequestorFullName,
            request.RequestorEmail,
            request.RequestedStartDate,
            request.RequestedEndDate,
        });

        await agent.RunAsync($"Validate this FOIA request:\n{payload}", cancellationToken: ct);
    }
}
