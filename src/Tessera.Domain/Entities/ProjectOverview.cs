namespace Tessera.Domain.Entities;

public class ProjectOverview
{
    public Guid Id { get; set; }
    public Guid RepositoryId { get; set; }
    public Guid SnapshotId { get; set; }
    public string Content { get; set; } = "";
    public string Model { get; set; } = "";
    public int NodeCount { get; set; }
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;

    public Repository? Repository { get; set; }
    public Snapshot? Snapshot { get; set; }
}
