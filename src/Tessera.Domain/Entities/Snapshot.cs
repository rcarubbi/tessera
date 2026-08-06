using Tessera.Domain.Enums;

namespace Tessera.Domain.Entities;

public class Snapshot
{
    public Guid Id { get; set; }
    public Guid RepositoryId { get; set; }
    public string CommitSha { get; set; } = "";
    public string RootHash { get; set; } = "";
    public int NodeCount { get; set; }
    public int EdgeCount { get; set; }
    public string? ParentCommitSha { get; set; }
    public ProcessingStatus Status { get; set; } = ProcessingStatus.Completed;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Repository? Repository { get; set; }
}
