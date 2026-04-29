using FoiaProcessor.Data;
using FoiaProcessor.Data.Audit;
using FoiaProcessor.Data.Entities;
using FoiaProcessor.McpTools.Contracts;
using Microsoft.EntityFrameworkCore;

namespace FoiaProcessor.McpTools.Servers;

public class ReviewServer
{
    private readonly FoiaDbContext _db;
    private readonly IAuditWriter _audit;

    public ReviewServer(FoiaDbContext db, IAuditWriter audit)
    {
        _db = db;
        _audit = audit;
    }

    public virtual async Task<CreateReviewTaskOutput> CreateReviewTaskAsync(CreateReviewTaskInput input, CancellationToken ct = default)
    {
        var task = new ReviewTask
        {
            FoiaRequestId = input.CaseId,
            Status = ReviewTaskStatus.Open,
            CreatedAt = DateTime.UtcNow,
        };
        _db.ReviewTasks.Add(task);
        await _db.SaveChangesAsync(ct);
        await _audit.RecordAsync(input.CaseId, AuditEventType.HumanReviewStarted, "Human review task opened.", ct: ct);
        return new CreateReviewTaskOutput(task.Id);
    }

    public virtual async Task<GetReviewStatusOutput> GetReviewStatusAsync(GetReviewStatusInput input, CancellationToken ct = default)
    {
        var docs = await _db.Documents
            .AsNoTracking()
            .Where(d => d.FoiaRequestId == input.CaseId)
            .Select(d => d.ReviewStatus)
            .ToListAsync(ct);

        var total = docs.Count;
        var approved = docs.Count(s => s == ReviewStatus.Approved);
        var rejected = docs.Count(s => s == ReviewStatus.ManualHandling);
        var pending = total - approved - rejected;
        var allApproved = total > 0 && approved == total;
        return new GetReviewStatusOutput(input.CaseId, total, approved, rejected, pending, allApproved);
    }

    public virtual async Task<ReviewActionOutput> RecordDocumentApprovalAsync(RecordDocumentApprovalInput input, CancellationToken ct = default)
    {
        var doc = await _db.Documents.FirstOrDefaultAsync(d => d.Id == input.DocumentId, ct)
            ?? throw new KeyNotFoundException($"Document {input.DocumentId} not found.");
        doc.ReviewStatus = ReviewStatus.Approved;
        await _db.SaveChangesAsync(ct);
        await _audit.RecordAsync(doc.FoiaRequestId, AuditEventType.DocumentApproved,
            string.IsNullOrWhiteSpace(input.Comments) ? $"Approved '{doc.FileName}'." : $"Approved '{doc.FileName}': {input.Comments}",
            doc.Id, ct);
        return new ReviewActionOutput(doc.Id, doc.ReviewStatus.ToString());
    }

    public virtual async Task<ReviewActionOutput> RecordDocumentRejectionAsync(RecordDocumentRejectionInput input, CancellationToken ct = default)
    {
        var doc = await _db.Documents.FirstOrDefaultAsync(d => d.Id == input.DocumentId, ct)
            ?? throw new KeyNotFoundException($"Document {input.DocumentId} not found.");
        doc.ReviewStatus = ReviewStatus.ManualHandling;
        await _db.SaveChangesAsync(ct);
        await _audit.RecordAsync(doc.FoiaRequestId, AuditEventType.DocumentRejected,
            $"Rejected '{doc.FileName}': {input.Comments}", doc.Id, ct);
        return new ReviewActionOutput(doc.Id, doc.ReviewStatus.ToString());
    }

    public virtual async Task<ReviewActionOutput> RecordReleaseApprovalAsync(RecordReleaseApprovalInput input, CancellationToken ct = default)
    {
        var task = await _db.ReviewTasks
            .Where(t => t.FoiaRequestId == input.CaseId && t.Status == ReviewTaskStatus.Open)
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (task is not null)
        {
            task.Status = ReviewTaskStatus.Completed;
            task.CompletedAt = DateTime.UtcNow;
            task.ReviewerDecision = ReviewerDecision.Approved;
            task.ReviewerComments = input.Comments;
            await _db.SaveChangesAsync(ct);
        }
        await _audit.RecordAsync(input.CaseId, AuditEventType.ReleaseApproved, "Reviewer approved release.", ct: ct);
        return new ReviewActionOutput(task?.Id ?? Guid.Empty, "Completed");
    }
}
