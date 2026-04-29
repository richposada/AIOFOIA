namespace FoiaProcessor.McpTools.Contracts;

public record CreateReviewTaskInput(Guid CaseId);
public record CreateReviewTaskOutput(Guid ReviewTaskId);

public record GetReviewStatusInput(Guid CaseId);
public record GetReviewStatusOutput(
    Guid CaseId,
    int TotalDocuments,
    int Approved,
    int Rejected,
    int Pending,
    bool AllApproved);

public record RecordDocumentApprovalInput(Guid DocumentId, string? Comments);
public record RecordDocumentRejectionInput(Guid DocumentId, string? Comments);
public record RecordReleaseApprovalInput(Guid CaseId, string? Comments);
public record ReviewActionOutput(Guid Id, string Status);
