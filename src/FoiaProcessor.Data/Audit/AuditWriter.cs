using FoiaProcessor.Data.Entities;
using Microsoft.Extensions.Logging;

namespace FoiaProcessor.Data.Audit;

public class AuditWriter : IAuditWriter
{
    private readonly FoiaDbContext _db;
    private readonly ILogger<AuditWriter> _logger;

    public AuditWriter(FoiaDbContext db, ILogger<AuditWriter> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<Guid> RecordAsync(
        Guid foiaRequestId,
        AuditEventType type,
        string message,
        Guid? relatedDocumentId = null,
        CancellationToken ct = default)
    {
        var evt = new AuditEvent
        {
            FoiaRequestId = foiaRequestId,
            RelatedDocumentId = relatedDocumentId,
            EventType = type,
            Message = message,
            Timestamp = DateTime.UtcNow,
        };
        _db.AuditEvents.Add(evt);
        await _db.SaveChangesAsync(ct);

        using (_logger.BeginScope(new Dictionary<string, object?>
        {
            ["RequestId"] = foiaRequestId,
            ["DocumentId"] = relatedDocumentId,
            ["AuditEventType"] = type.ToString(),
        }))
        {
            _logger.LogInformation("[Audit] {EventType} - {Message}", type, message);
        }

        return evt.Id;
    }
}
