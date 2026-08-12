using Tessera.Domain.Enums;

namespace Tessera.Domain.Entities;

public class PullRequestReview
{
    public Guid Id { get; set; }
    public Guid RepositoryId { get; set; }
    public int PrNumber { get; set; }
    public string HeadSha { get; set; } = "";
    public string BaseSha { get; set; } = "";
    public PrReviewStatus Status { get; set; } = PrReviewStatus.Queued;
    public long? CommentId { get; set; }
    public string? CommentBody { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
