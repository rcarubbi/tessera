using Tessera.Domain.Enums;

namespace Tessera.Domain.Entities;

public class KnowledgeNode
{
    public Guid Id { get; set; }
    public Guid RepositoryId { get; set; }
    public Guid SnapshotId { get; set; }
    public string Key { get; set; } = "";
    public string Path { get; set; } = "";
    public string Symbol { get; set; } = "";
    public NodeKind Kind { get; set; }
    public string Language { get; set; } = "";
    public int StartLine { get; set; }
    public int EndLine { get; set; }

    public string StructuralHash { get; set; } = "";
    public string SemanticHash { get; set; } = "";
    public string? ParentSemanticHash { get; set; }

    public string Content { get; set; } = "";
    public double Confidence { get; set; } = 1.0;
    public ReviewStatus ReviewStatus { get; set; } = ReviewStatus.None;

    public string CommitSha { get; set; } = "";
    public string? Model { get; set; }
    public string? PromptVersion { get; set; }
    public DateTimeOffset AnalyzedAt { get; set; }

    public DateTimeOffset? EditedAt { get; set; }
    public string? EditedBy { get; set; }

    public Repository? Repository { get; set; }
    public Snapshot? Snapshot { get; set; }
}
