namespace FoiaProcessor.McpTools.Contracts;

public record SearchDocumentsInput(string Query, DateOnly? StartDate, DateOnly? EndDate, int? Top);

public record SearchResultItem(
    string chunk_id,
    string? title,
    string file_name,
    string file_type,
    string? source_uri,
    DateOnly? document_date,
    string? chunk,
    IReadOnlyDictionary<string, string>? Metadata);

public record SearchDocumentsOutput(IReadOnlyList<SearchResultItem> Results);

public record GetDocumentByIdInput(string Id);
public record GetDocumentContentOutput(string Id, string Content);

public record SearchByDateRangeInput(DateOnly StartDate, DateOnly EndDate, int? Top);
