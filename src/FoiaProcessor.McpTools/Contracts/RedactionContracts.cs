namespace FoiaProcessor.McpTools.Contracts;

public record DetectPiiInput(string Text, bool UseAi);

public record PiiFinding(
    string PiiType,
    string OriginalText,
    int StartOffset,
    int EndOffset,
    double? Confidence,
    string DetectionSource);

public record DetectPiiOutput(IReadOnlyList<PiiFinding> Findings);

public record RedactTextInput(string Text, IReadOnlyList<PiiFinding> Findings);

public record AppliedFinding(
    string PiiType,
    string OriginalText,
    string ReplacementText,
    int StartOffset,
    int EndOffset,
    double? Confidence,
    string DetectionSource);

public record RedactTextOutput(string RedactedText, IReadOnlyList<AppliedFinding> AppliedFindings);

public record CreateRedactionReportInput(Guid DocumentId, IReadOnlyList<AppliedFinding> Findings);
public record RedactionReportItem(
    Guid Id,
    string PiiType,
    string OriginalText,
    string ReplacementText,
    int? StartOffset,
    int? EndOffset,
    double? Confidence,
    string DetectionSource);

public record CreateRedactionReportOutput(Guid DocumentId, IReadOnlyList<RedactionReportItem> Redactions);

public record SaveRedactedDocumentInput(
    Guid DocumentId,
    string RedactedContent,
    IReadOnlyList<AppliedFinding> AppliedFindings);
