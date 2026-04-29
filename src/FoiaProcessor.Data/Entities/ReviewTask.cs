namespace FoiaProcessor.Data.Entities;

public class ReviewTask
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FoiaRequestId { get; set; }
    public FoiaRequest? FoiaRequest { get; set; }

    public string AssignedReviewer { get; set; } = "demo-reviewer";
    public ReviewTaskStatus Status { get; set; } = ReviewTaskStatus.Open;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public ReviewerDecision? ReviewerDecision { get; set; }
    public string? ReviewerComments { get; set; }
}
