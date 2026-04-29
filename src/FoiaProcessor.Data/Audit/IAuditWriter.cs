using FoiaProcessor.Data.Entities;

namespace FoiaProcessor.Data.Audit;

public interface IAuditWriter
{
    Task<Guid> RecordAsync(
        Guid foiaRequestId,
        AuditEventType type,
        string message,
        Guid? relatedDocumentId = null,
        CancellationToken ct = default);
}
