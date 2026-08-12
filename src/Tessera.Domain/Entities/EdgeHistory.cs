using Tessera.Domain.Enums;

namespace Tessera.Domain.Entities;

public class EdgeHistory
{
    public Guid Id { get; set; }
    public Guid RepositoryId { get; set; }

    public string FromKey { get; set; } = "";
    public string ToKey { get; set; } = "";
    public EdgeType Type { get; set; }

    public Guid IntroducedSnapshotId { get; set; }
    public string IntroducedCommitSha { get; set; } = "";
    public DateTimeOffset IntroducedAt { get; set; } = DateTimeOffset.UtcNow;

    public bool Live { get; set; } = true;
}
