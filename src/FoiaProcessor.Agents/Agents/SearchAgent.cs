using System.ComponentModel;
using System.Text.Json;
using FoiaProcessor.Agents.Infrastructure;
using FoiaProcessor.Data;
using FoiaProcessor.Data.Entities;
using FoiaProcessor.McpTools.Contracts;
using FoiaProcessor.McpTools.Options;
using FoiaProcessor.McpTools.Servers;
using Microsoft.Agents.AI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace FoiaProcessor.Agents.Agents;

/// <summary>
/// Agent 2 (Microsoft Agent Framework): The LLM crafts a search query, runs it
/// against the SearchServer MCP tool, fetches each result's content, and
/// persists matches via CaseServer tools.
/// </summary>
public class SearchAgent
{
    private readonly CaseServer _case;
    private readonly SearchServer _search;
    private readonly FoiaDbContext _db;
    private readonly WorkflowOptions _wfOpts;
    private readonly AgentChatClientFactory _chatFactory;

    public SearchAgent(
        CaseServer caseServer,
        SearchServer searchServer,
        FoiaDbContext db,
        IOptions<WorkflowOptions> wfOpts,
        AgentChatClientFactory chatFactory)
    {
        _case = caseServer;
        _search = searchServer;
        _db = db;
        _wfOpts = wfOpts.Value;
        _chatFactory = chatFactory;
    }

    public virtual async Task RunAsync(Guid requestId, CancellationToken ct = default)
    {
        var request = await _db.FoiaRequests.AsNoTracking().FirstOrDefaultAsync(r => r.Id == requestId, ct)
            ?? throw new KeyNotFoundException($"FoiaRequest {requestId} not found.");

        await _case.CaseUpdateStatusAsync(
            new CaseUpdateStatusInput(requestId, nameof(RequestStatus.Searching), "Search starting."),
            ct);

        // Server-side cache so the LLM only has to pass IDs around — no JSON marshalling.
        var foundResults = new Dictionary<string, SearchResultItem>(StringComparer.Ordinal);
        var fetchedContent = new Dictionary<string, string>(StringComparer.Ordinal);

        [Description("Run a keyword search against the document index. Returns matching document IDs, titles, and file types.")]
        async Task<string> SearchDocuments(
            [Description("Free-text query (use the request subject and salient keywords).")] string query)
        {
            var output = await _search.SearchDocumentsAsync(
                new SearchDocumentsInput(query, request.RequestedStartDate, request.RequestedEndDate, _wfOpts.MaxSearchResults),
                ct);

            foundResults.Clear();
            fetchedContent.Clear();
            foreach (var r in output.Results)
            {
                foundResults[r.chunk_id] = r;
            }

            return JsonSerializer.Serialize(output.Results);
        }

        [Description("Fetch the full text content of one document by ID. Call this for every document returned by SearchDocuments before saving.")]
        async Task<string> GetDocumentContent(
            [Description("Document ID returned by SearchDocuments.")] string documentId)
        {
            var output = await _search.GetDocumentContentAsync(new GetDocumentByIdInput(documentId), ct);
            fetchedContent[documentId] = output.Content ?? string.Empty;
            return JsonSerializer.Serialize(new { documentId, content = output.Content });
        }

        [Description("Persist every document returned by SearchDocuments whose content was fetched via GetDocumentContent. Takes no arguments. Call this exactly once after fetching all contents.")]
        async Task<string> SaveDocuments()
        {
            var docs = foundResults.Values
                .Where(r => fetchedContent.ContainsKey(r.chunk_id))
                .Select(r => new SaveDocumentInput(
                    SourceDocumentId: r.chunk_id,
                    FileName: r.file_name,
                    FileType: r.file_type,
                    SourceUri: r.source_uri,
                    OriginalContent: fetchedContent[r.chunk_id]))
                .ToList();

            if (docs.Count == 0)
            {
                return "no-documents-to-save";
            }

            await _case.CaseSaveDocumentMetadataAsync(new CaseSaveDocumentMetadataInput(requestId, docs), ct);
            await _case.CaseUpdateStatusAsync(
                new CaseUpdateStatusInput(requestId, nameof(RequestStatus.DocumentsFound), $"{docs.Count} document(s) found."),
                ct);
            return $"saved {docs.Count}";
        }

        [Description("Mark the request as having no matching documents. Call this only when SearchDocuments returns an empty result set.")]
        async Task<string> MarkNoDocumentsFound()
        {
            await _case.CaseUpdateStatusAsync(
                new CaseUpdateStatusInput(requestId, nameof(RequestStatus.NoDocumentsFound), "No matching documents found."),
                ct);
            return "no-documents";
        }

        var agent = new ChatClientAgent(
            _chatFactory.ChatClient,
            instructions: """
            You are the Document Search agent. Your job:
            1. Build a concise keyword query from the request Subject and Description (nouns and proper names work best).
            2. Call SearchDocuments(query).
            3. If it returns zero results, call MarkNoDocumentsFound and stop.
            4. Otherwise, call GetDocumentContent once for EACH document ID returned by SearchDocuments.
            5. Call SaveDocuments (no arguments). The server already has the metadata and content cached.
            6. Stop. Do not produce any further commentary.
            """,
            name: "DocumentSearch",
            description: null,
            tools: new List<AITool>
            {
                AIFunctionFactory.Create((Delegate)SearchDocuments),
                AIFunctionFactory.Create((Delegate)GetDocumentContent),
                AIFunctionFactory.Create((Delegate)SaveDocuments),
                AIFunctionFactory.Create((Delegate)MarkNoDocumentsFound),
            });

        var payload = JsonSerializer.Serialize(new
        {
            request.Subject,
            request.Description,
            request.RequestedStartDate,
            request.RequestedEndDate,
        });

        await agent.RunAsync($"Find documents responsive to this FOIA request:\n{payload}", cancellationToken: ct);
    }
}
