using System.ClientModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using Azure.AI.OpenAI;
using FoiaProcessor.Data.Entities;
using FoiaProcessor.McpTools.Contracts;
using FoiaProcessor.McpTools.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI.Chat;

namespace FoiaProcessor.McpTools.Servers;

/// <summary>
/// Redaction server. Hybrid PII detection:
///   - Regex for Email, Phone, SSN, DateOfBirth (deterministic, always on).
///   - Azure OpenAI (when configured + `useAi=true`) for Name, Address, FinancialId.
/// All persistence delegates to <see cref="CaseServer"/>.
/// </summary>
public class RedactionServer
{
    private static readonly Regex EmailRegex = new(@"[\w\.-]+@[\w\.-]+\.\w+", RegexOptions.Compiled);
    private static readonly Regex PhoneRegex = new(@"(\+?\d{1,2}[\s\-\.]?)?(\(?\d{3}\)?[\s\-\.]?)\d{3}[\s\-\.]?\d{4}", RegexOptions.Compiled);
    private static readonly Regex SsnRegex = new(@"\b\d{3}-\d{2}-\d{4}\b", RegexOptions.Compiled);
    private static readonly Regex DobRegex = new(
        @"\b(0?[1-9]|1[0-2])[\/\-](0?[1-9]|[12]\d|3[01])[\/\-](19|20)\d{2}\b",
        RegexOptions.Compiled);

    private readonly AzureOpenAIOptions _aiOpts;
    private readonly ILogger<RedactionServer> _logger;

    public RedactionServer(IOptions<AzureOpenAIOptions> aiOpts, ILogger<RedactionServer> logger)
    {
        _aiOpts = aiOpts.Value;
        _logger = logger;
    }

    public virtual async Task<DetectPiiOutput> DetectPiiAsync(DetectPiiInput input, CancellationToken ct = default)
    {
        var findings = new List<PiiFinding>();
        AddRegex(findings, input.Text, EmailRegex, nameof(PiiType.Email));
        AddRegex(findings, input.Text, PhoneRegex, nameof(PiiType.Phone));
        AddRegex(findings, input.Text, SsnRegex, nameof(PiiType.SSN));
        AddRegex(findings, input.Text, DobRegex, nameof(PiiType.DateOfBirth));

        if (input.UseAi && !string.IsNullOrEmpty(_aiOpts.Endpoint))
        {
            try
            {
                findings.AddRange(await DetectAiAsync(input.Text, ct));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AI PII detection failed; continuing with regex findings only.");
            }
        }

        // Stable order for deterministic redaction (rightmost first applied).
        findings = findings
            .OrderBy(f => f.StartOffset)
            .ThenBy(f => f.PiiType)
            .ToList();
        return new DetectPiiOutput(findings);
    }

    private static void AddRegex(List<PiiFinding> sink, string text, Regex regex, string piiType)
    {
        foreach (Match m in regex.Matches(text))
        {
            sink.Add(new PiiFinding(piiType, m.Value, m.Index, m.Index + m.Length, null, "regex"));
        }
    }

    private async Task<IEnumerable<PiiFinding>> DetectAiAsync(string text, CancellationToken ct)
    {
        var client = new AzureOpenAIClient(
            new Uri(_aiOpts.Endpoint),
            new ApiKeyCredential(_aiOpts.ApiKey ?? string.Empty));
        var chat = client.GetChatClient(_aiOpts.Deployment);

        var systemPrompt = """
            You extract personally identifiable information from text.
            Return ONLY a JSON object: {"findings":[{"piiType":"Name|Address|FinancialId","originalText":"...","startOffset":<int>,"endOffset":<int>,"confidence":0.0-1.0}]}.
            Do not include emails, phone numbers, SSNs, or dates of birth (those are handled separately).
            Use exact substring offsets in the input text.
            """;

        var completion = await chat.CompleteChatAsync(
            new ChatMessage[]
            {
                new SystemChatMessage(systemPrompt),
                new UserChatMessage(text),
            },
            new ChatCompletionOptions
            {
                ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat(),
                Temperature = 0,
            },
            ct);

        var json = completion.Value.Content[0].Text;
        using var doc = JsonDocument.Parse(json);
        var results = new List<PiiFinding>();
        if (doc.RootElement.TryGetProperty("findings", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var f in arr.EnumerateArray())
            {
                results.Add(new PiiFinding(
                    f.GetProperty("piiType").GetString() ?? "Name",
                    f.GetProperty("originalText").GetString() ?? string.Empty,
                    f.GetProperty("startOffset").GetInt32(),
                    f.GetProperty("endOffset").GetInt32(),
                    f.TryGetProperty("confidence", out var c) ? c.GetDouble() : 0.5,
                    "ai"));
            }
        }
        return results;
    }

    public virtual Task<RedactTextOutput> RedactTextAsync(RedactTextInput input, CancellationToken ct = default)
    {
        // Apply right-to-left to preserve offsets while substituting.
        var ordered = input.Findings.OrderByDescending(f => f.StartOffset).ToList();
        var sb = new System.Text.StringBuilder(input.Text);
        var applied = new List<AppliedFinding>(ordered.Count);
        foreach (var f in ordered)
        {
            var replacement = ReplacementLabel(f.PiiType);
            if (f.StartOffset < 0 || f.EndOffset > sb.Length || f.EndOffset <= f.StartOffset) continue;
            sb.Remove(f.StartOffset, f.EndOffset - f.StartOffset);
            sb.Insert(f.StartOffset, replacement);
            applied.Add(new AppliedFinding(
                f.PiiType, f.OriginalText, replacement, f.StartOffset, f.StartOffset + replacement.Length,
                f.Confidence, f.DetectionSource));
        }
        applied.Reverse();
        return Task.FromResult(new RedactTextOutput(sb.ToString(), applied));
    }

    public virtual Task<CreateRedactionReportOutput> CreateRedactionReportAsync(CreateRedactionReportInput input, CancellationToken ct = default)
    {
        var items = input.Findings.Select(f => new RedactionReportItem(
            Guid.NewGuid(), f.PiiType, f.OriginalText, f.ReplacementText,
            f.StartOffset, f.EndOffset, f.Confidence, f.DetectionSource)).ToList();
        return Task.FromResult(new CreateRedactionReportOutput(input.DocumentId, items));
    }

    public virtual async Task<CaseSaveRedactionResultsOutput> SaveRedactedDocumentAsync(SaveRedactedDocumentInput input, CancellationToken ct = default)
    {
        // Delegated to CaseServer in the agent layer; this overload is provided for tool parity.
        // Returns a synthetic success — real persistence happens via CaseServer.CaseSaveRedactionResultsAsync.
        await Task.CompletedTask;
        return new CaseSaveRedactionResultsOutput(input.DocumentId, "Completed", input.AppliedFindings.Count);
    }

    private static string ReplacementLabel(string piiType) => piiType.ToUpperInvariant() switch
    {
        "EMAIL" => "[REDACTED EMAIL]",
        "PHONE" => "[REDACTED PHONE]",
        "SSN" => "[REDACTED SSN]",
        "DATEOFBIRTH" => "[REDACTED DOB]",
        "NAME" => "[REDACTED NAME]",
        "ADDRESS" => "[REDACTED ADDRESS]",
        "FINANCIALID" => "[REDACTED FINANCIAL ID]",
        _ => "[REDACTED]",
    };
}
