using System.ComponentModel;
using System.Text.Json;
using FoiaProcessor.Agents.Infrastructure;
using FoiaProcessor.McpTools.Contracts;
using FoiaProcessor.McpTools.Servers;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace FoiaProcessor.Agents.Agents;

/// <summary>
/// Agent 4 (Microsoft Agent Framework): Decides whether a review task already
/// exists for this request and opens one if needed. The workflow runner pauses
/// after this agent; the API resumes it after release approval.
/// </summary>
public class HumanReviewCoordinatorAgent
{
    private readonly ReviewServer _review;
    private readonly AgentChatClientFactory _chatFactory;

    public HumanReviewCoordinatorAgent(ReviewServer reviewServer, AgentChatClientFactory chatFactory)
    {
        _review = reviewServer;
        _chatFactory = chatFactory;
    }

    public virtual async Task RunAsync(Guid requestId, CancellationToken ct = default)
    {
        [Description("Get the current review status for this request: total documents, pending count, approved count.")]
        async Task<string> GetReviewStatus()
        {
            var status = await _review.GetReviewStatusAsync(new GetReviewStatusInput(requestId), ct);
            return JsonSerializer.Serialize(status);
        }

        [Description("Create a new review task for this request. Call only when no review task exists yet (Pending == TotalDocuments and TotalDocuments > 0).")]
        async Task<string> CreateReviewTask()
        {
            await _review.CreateReviewTaskAsync(new CreateReviewTaskInput(requestId), ct);
            return "review-task-created";
        }

        [Description("Acknowledge that a review task already exists; no further action needed in this run.")]
        string Acknowledge() => "ok";

        var agent = new ChatClientAgent(
            _chatFactory.ChatClient,
            instructions: """
            You are the Human Review Coordinator. Decide whether to open a review task:
            1. Call GetReviewStatus.
            2. If the response shows TotalDocuments > 0 AND Pending == TotalDocuments, call CreateReviewTask.
            3. Otherwise call Acknowledge.
            4. Stop.
            """,
            name: "HumanReviewCoordinator",
            description: null,
            tools: new List<AITool>
            {
                AIFunctionFactory.Create((Delegate)GetReviewStatus),
                AIFunctionFactory.Create((Delegate)CreateReviewTask),
                AIFunctionFactory.Create((Delegate)Acknowledge),
            });

        await agent.RunAsync($"Coordinate human review for request {requestId}.", cancellationToken: ct);
    }
}
