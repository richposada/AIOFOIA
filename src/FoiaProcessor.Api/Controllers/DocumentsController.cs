using FoiaProcessor.Agents.Workflow;
using FoiaProcessor.Api.Contracts;
using FoiaProcessor.Data;
using FoiaProcessor.Data.Entities;
using FoiaProcessor.McpTools.Contracts;
using FoiaProcessor.McpTools.Servers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FoiaProcessor.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/documents")]
public class DocumentsController : ControllerBase
{
    private readonly FoiaDbContext _db;
    private readonly ReviewServer _review;
    private readonly WorkflowQueue _queue;

    public DocumentsController(FoiaDbContext db, ReviewServer reviewServer, WorkflowQueue queue)
    {
        _db = db;
        _review = reviewServer;
        _queue = queue;
    }

    [HttpGet("{id:guid}/review")]
    public async Task<IActionResult> GetReview(Guid id, CancellationToken ct)
    {
        var doc = await _db.Documents
            .AsNoTracking()
            .Include(d => d.Redactions)
            .FirstOrDefaultAsync(d => d.Id == id, ct);
        if (doc is null) return NotFound(new { title = "Not Found", status = 404 });

        var dto = new DocumentReviewDto(
            doc.Id,
            doc.FoiaRequestId,
            doc.FileName,
            doc.OriginalContent,
            doc.RedactedContent,
            doc.Redactions
                .OrderBy(r => r.StartOffset)
                .Select(r => new DocumentRedactionDto(
                    r.Id, r.PiiType.ToString(), r.OriginalText, r.ReplacementText,
                    r.StartOffset, r.EndOffset, r.PageNumber, r.Confidence,
                    r.DetectionSource.ToString(), r.ReviewerApproved))
                .ToList(),
            doc.ReviewStatus.ToString());
        return Ok(dto);
    }

    [HttpPost("{id:guid}/approve")]
    public async Task<IActionResult> Approve(Guid id, [FromBody] ApproveDocumentRequestDto dto, CancellationToken ct)
    {
        var doc = await _db.Documents.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (doc is null) return NotFound(new { title = "Not Found", status = 404 });

        await _review.RecordDocumentApprovalAsync(new RecordDocumentApprovalInput(id, dto?.Comments), ct);

        return Ok(new ApproveDocumentResponseDto(id, nameof(ReviewStatus.Approved), DateTime.UtcNow));
    }

    [HttpPost("{id:guid}/reject")]
    public async Task<IActionResult> Reject(Guid id, [FromBody] RejectDocumentRequestDto dto, CancellationToken ct)
    {
        if (dto is null || string.IsNullOrWhiteSpace(dto.Comments))
        {
            return BadRequest(new
            {
                title = "Comments are required when rejecting a document.",
                status = 400,
                errors = new Dictionary<string, string[]> { ["comments"] = new[] { "Comments are required." } },
            });
        }

        var doc = await _db.Documents.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (doc is null) return NotFound(new { title = "Not Found", status = 404 });

        await _review.RecordDocumentRejectionAsync(new RecordDocumentRejectionInput(id, dto.Comments), ct);

        return Ok(new RejectDocumentResponseDto(id, nameof(ReviewStatus.ManualHandling), DateTime.UtcNow));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var doc = await _db.Documents
            .Include(d => d.Redactions)
            .FirstOrDefaultAsync(d => d.Id == id, ct);
        if (doc is null) return NotFound(new { title = "Not Found", status = 404 });

        _db.Documents.Remove(doc);
        await _db.SaveChangesAsync(ct);

        return NoContent();
    }
}
