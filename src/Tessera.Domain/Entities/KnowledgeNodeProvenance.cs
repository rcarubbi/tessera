namespace Tessera.Domain.Entities;

public class KnowledgeNodeProvenance
{
    public Guid Id { get; set; }
    public Guid NodeId { get; set; }
    public string CommitSha { get; set; } = "";
    public string Model { get; set; } = "";
    public string PromptVersion { get; set; } = "";
    public DateTimeOffset GeneratedAt { get; set; }
    public string? EditedBy { get; set; }
    public DateTimeOffset? EditedAt { get; set; }
    public string? PreviousSemanticHash { get; set; }
}
