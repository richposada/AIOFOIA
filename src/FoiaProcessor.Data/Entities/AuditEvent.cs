namespace FoiaProcessor.Data.Entities;

public class AuditEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FoiaRequestId { get; set; }
    public FoiaRequest? FoiaRequest { get; set; }
    public Guid? RelatedDocumentId { get; set; }

    public AuditEventType EventType { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string Message { get; set; } = string.Empty;
}
