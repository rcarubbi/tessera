using Tessera.Domain.Enums;

namespace Tessera.Infrastructure.Queries;

public static class ReviewStatusLabel
{
    public static string Get(ReviewStatus status) => status switch
    {
        ReviewStatus.NeedsReview => "needs_review",
        ReviewStatus.Stale => "stale",
        ReviewStatus.Accepted => "accepted",
        ReviewStatus.Edited => "edited",
        _ => "none"
    };
}
