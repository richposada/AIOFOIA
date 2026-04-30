using FoiaProcessor.Agents.Workflow;
using FoiaProcessor.Api.Contracts;
using FoiaProcessor.Api.Validation;
using FoiaProcessor.Data;
using FoiaProcessor.Data.Entities;
using FoiaProcessor.McpTools.Contracts;
using FoiaProcessor.McpTools.Servers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FoiaProcessor.Api.Controllers;

[ApiController]
[Route("api/foiarequests")]
public class FoiaRequestsController : ControllerBase
{
    private readonly CaseServer _case;
    private readonly ReviewServer _review;
    private readonly FoiaDbContext _db;
    private readonly WorkflowQueue _queue;

    public FoiaRequestsController(CaseServer caseServer, ReviewServer reviewServer, FoiaDbContext db, WorkflowQueue queue)
    {
        _case = caseServer;
        _review = reviewServer;
        _db = db;
        _queue = queue;
    }

    [HttpPost]
    public async Task<IActionResult> Submit([FromBody] SubmitFoiaRequestDto dto, CancellationToken ct)
    {
        var errors = FoiaRequestValidator.Validate(dto);
        if (errors.Count > 0)
        {
            return BadRequest(new
            {
                title = "One or more validation errors occurred.",
                status = 400,
                errors,
            });
        }

        var created = await _case.CaseCreateAsync(new CaseCreateInput(
            dto.Subject,
            dto.Description,
            dto.RequestedStartDate,
            dto.RequestedEndDate,
            dto.RequestorFullName,
            dto.RequestorOrganization,
            dto.RequestorEmail,
            dto.RequestorPhone,
            dto.RequestorMailingAddress), ct);

        await _queue.EnqueueAsync(created.CaseId, ct);

        var submittedAt = await _db.FoiaRequests
            .Where(r => r.Id == created.CaseId)
            .Select(r => r.SubmittedAt)
            .FirstAsync(ct);

        return Ok(new SubmitFoiaRequestResponseDto(created.CaseId, created.Status, submittedAt));
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int take, CancellationToken ct)
    {
        var clamped = Math.Clamp(take <= 0 ? 10 : take, 1, 100);
        var summaries = await _db.FoiaRequests
            .AsNoTracking()
            .OrderByDescending(r => r.SubmittedAt)
            .Take(clamped)
            .Select(r => new FoiaRequestSummaryDto(
                r.Id,
                r.Subject,
                r.RequestorFullName,
                r.Status.ToString(),
                r.SubmittedAt))
            .ToListAsync(ct);
        return Ok(summaries);
    }

    [HttpGet("pending-review")]
    public async Task<IActionResult> ListPendingReview(CancellationToken ct)
    {
        var summaries = await _db.FoiaRequests
            .AsNoTracking()
            .Where(r => r.Status == RequestStatus.PendingHumanReview)
            .OrderByDescending(r => r.SubmittedAt)
            .Select(r => new FoiaRequestSummaryDto(
                r.Id,
                r.Subject,
                r.RequestorFullName,
                r.Status.ToString(),
                r.SubmittedAt))
            .ToListAsync(ct);
        return Ok(summaries);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var request = await _db.FoiaRequests
            .AsNoTracking()
            .Include(r => r.Documents)
            .Include(r => r.ReleasePackage)
            .FirstOrDefaultAsync(r => r.Id == id, ct);

        if (request is null)
        {
            return NotFound(new { title = "Not Found", status = 404, detail = $"FoiaRequest {id} not found." });
        }

        var auditEvents = await _db.AuditEvents
            .AsNoTracking()
            .Where(e => e.FoiaRequestId == id)
            .OrderBy(e => e.Timestamp)
            .Select(e => new
            {
                e.Timestamp,
                e.EventType,
                e.Message,
                e.RelatedDocumentId,
                FileName = e.RelatedDocumentId == null
                    ? null
                    : _db.Documents.Where(d => d.Id == e.RelatedDocumentId).Select(d => d.FileName).FirstOrDefault(),
            })
            .ToListAsync(ct);

        var counts = new CountsDto(
            DocumentsFound: request.Documents.Count,
            DocumentsPendingReview: request.Documents.Count(d => d.ReviewStatus == ReviewStatus.Pending || d.ReviewStatus == ReviewStatus.NotStarted),
            DocumentsApproved: request.Documents.Count(d => d.ReviewStatus == ReviewStatus.Approved),
            DocumentsRejected: request.Documents.Count(d => d.ReviewStatus == ReviewStatus.ManualHandling));

        var release = request.ReleasePackage is { } pkg
            ? new ReleaseDto("Ready", pkg.SasUrl, pkg.SasExpiresAt)
            : new ReleaseDto("NotReady", null, null);

        var dto = new FoiaRequestStatusDto(
            request.Id,
            request.Subject,
            request.RequestorFullName,
            request.RequestorEmail,
            request.RequestedStartDate,
            request.RequestedEndDate,
            request.Status.ToString(),
            request.SubmittedAt,
            counts,
            release,
            auditEvents.Select(e => new AuditEventDto(
                e.Timestamp, e.EventType.ToString(), e.Message, e.RelatedDocumentId, e.FileName)).ToList());

        return Ok(dto);
    }

    [HttpGet("{id:guid}/documents")]
    public async Task<IActionResult> ListDocuments(Guid id, CancellationToken ct)
    {
        var exists = await _db.FoiaRequests.AnyAsync(r => r.Id == id, ct);
        if (!exists) return NotFound(new { title = "Not Found", status = 404 });

        var docs = await _db.Documents
            .AsNoTracking()
            .Where(d => d.FoiaRequestId == id)
            .Select(d => new DocumentSummaryDto(
                d.Id,
                d.FileName,
                d.FileType,
                d.RedactionStatus.ToString(),
                d.ReviewStatus.ToString(),
                d.Redactions.Count))
            .ToListAsync(ct);

        return Ok(new DocumentsListDto(id, docs));
    }

    [HttpPost("{id:guid}/approve-release")]
    public async Task<IActionResult> ApproveRelease(Guid id, CancellationToken ct)
    {
        var request = await _db.FoiaRequests
            .Include(r => r.Documents)
            .FirstOrDefaultAsync(r => r.Id == id, ct);
        if (request is null) return NotFound(new { title = "Not Found", status = 404 });

        var notApproved = request.Documents.Any(d => d.ReviewStatus != ReviewStatus.Approved);
        if (notApproved)
        {
            return Conflict(new
            {
                title = "Not all documents approved.",
                status = 409,
                detail = "Every document must be approved before the release can be approved.",
            });
        }

        await _review.RecordReleaseApprovalAsync(new RecordReleaseApprovalInput(id, null), ct);
        await _case.CaseUpdateStatusAsync(
            new CaseUpdateStatusInput(id, nameof(RequestStatus.ApprovedForRelease), "Reviewer approved release."),
            ct);
        await _queue.EnqueueAsync(id, ct);

        return Ok(new ApproveReleaseResponseDto(id, nameof(RequestStatus.ApprovedForRelease), DateTime.UtcNow));
    }

    [HttpGet("{id:guid}/release")]
    public async Task<IActionResult> GetRelease(Guid id, CancellationToken ct)
    {
        var pkg = await _db.ReleasePackages
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.FoiaRequestId == id, ct);
        if (pkg is null)
        {
            return Ok(new ReleasePackageDto(id, "NotReady", null, null, null, null, null));
        }
        return Ok(new ReleasePackageDto(
            id, "Ready", pkg.ZipBlobName, pkg.BlobContainerName, pkg.SasUrl, pkg.SasExpiresAt, pkg.CreatedAt));
    }
}
