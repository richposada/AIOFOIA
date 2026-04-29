namespace FoiaProcessor.Data.Entities;

public class Redaction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DocumentId { get; set; }
    public Document? Document { get; set; }

    public PiiType PiiType { get; set; }
    public string OriginalText { get; set; } = string.Empty;
    public string ReplacementText { get; set; } = string.Empty;
    public int? StartOffset { get; set; }
    public int? EndOffset { get; set; }
    public int? PageNumber { get; set; }
    public double? Confidence { get; set; }
    public DetectionSource DetectionSource { get; set; }
    public bool? ReviewerApproved { get; set; }
    public string? ReviewerComments { get; set; }
}
