using Tessera.Domain.Entities;
using Tessera.Domain.Enums;
using Tessera.Domain.Merkle;
using Tessera.Domain.Parsing;

namespace Tessera.Domain.Merkle;

public sealed class AiContent
{
    public required string Content { get; init; }
    public string? ClassDiagram { get; init; }
    public string? SequenceDiagram { get; init; }
    public double Confidence { get; init; } = 1.0;
    public string Model { get; init; } = "";
    public string PromptVersion { get; init; } = "";
}

public sealed class ComposedSnapshot
{
    public List<KnowledgeNode> Nodes { get; } = new();
    public List<GraphEdge> Edges { get; } = new();
    public string RootHash { get; set; } = "";
}

public static class SnapshotComposer
{
    public const string PendingContent = "// analysis pending";

    public static ComposedSnapshot Compose(
        Guid repositoryId,
        Guid snapshotId,
        string commitSha,
        ParseResult parse,
        IReadOnlyDictionary<string, KnowledgeNode> previousNodes,
        IReadOnlyDictionary<string, AiContent> aiContent)
    {
        var nodes = new Dictionary<string, KnowledgeNode>(StringComparer.Ordinal);
        var aiKeys = new HashSet<string>(aiContent.Keys, StringComparer.Ordinal);

        foreach (var entity in parse.Entities)
        {
            var needsAi = aiKeys.Contains(entity.Key);
            AiContent? generated = null;
            if (needsAi)
            {
                aiContent.TryGetValue(entity.Key, out generated);
            }

            KnowledgeNode node;
            if (!needsAi && previousNodes.TryGetValue(entity.Key, out var previous))
            {
                node = new KnowledgeNode
                {
                    Id = Guid.NewGuid(),
                    RepositoryId = repositoryId,
                    SnapshotId = snapshotId,
                    Key = entity.Key,
                    Path = entity.Path,
                    Symbol = entity.Symbol,
                    Kind = entity.Kind,
                    Language = entity.Language,
                    StartLine = entity.StartLine,
                    EndLine = entity.EndLine,
                    StructuralHash = entity.StructuralHash,
                    Content = previous.Content,
                    ClassDiagram = previous.ClassDiagram,
                    SequenceDiagram = previous.SequenceDiagram,
                    Confidence = previous.Confidence,
                    ReviewStatus = previous.ReviewStatus,
                    CommitSha = commitSha,
                    Model = previous.Model,
                    PromptVersion = previous.PromptVersion,
                    AnalyzedAt = previous.AnalyzedAt
                };
            }
            else
            {
                node = new KnowledgeNode
                {
                    Id = Guid.NewGuid(),
                    RepositoryId = repositoryId,
                    SnapshotId = snapshotId,
                    Key = entity.Key,
                    Path = entity.Path,
                    Symbol = entity.Symbol,
                    Kind = entity.Kind,
                    Language = entity.Language,
                    StartLine = entity.StartLine,
                    EndLine = entity.EndLine,
                    StructuralHash = entity.StructuralHash,
                    Content = generated?.Content ?? PendingContent,
                    ClassDiagram = generated?.ClassDiagram,
                    SequenceDiagram = generated?.SequenceDiagram,
                    Confidence = generated?.Confidence ?? 0.0,
                    CommitSha = commitSha,
                    Model = generated?.Model,
                    PromptVersion = generated?.PromptVersion,
                    AnalyzedAt = DateTimeOffset.UtcNow
                };
            }

            nodes[entity.Key] = node;
        }

        var childGroups = parse.Relationships
            .GroupBy(r => r.From, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var dagNodes = nodes.Values.Select(n =>
        {
            var children = childGroups.TryGetValue(n.Key, out var rels)
                ? rels
                    .Where(r => nodes.ContainsKey(r.To))
                    .Select(r => new ChildHash(r.To, r.Type.ToString(), ""))
                    .ToList()
                : new List<ChildHash>();
            return new DagNode { Key = n.Key, Content = n.Content, Children = children };
        });

        var hashes = MerkleDag.ComputeHashes(dagNodes);

        foreach (var node in nodes.Values)
        {
            node.SemanticHash = hashes[node.Key];
        }

        var edges = new List<GraphEdge>();
        foreach (var rel in parse.Relationships)
        {
            if (!nodes.TryGetValue(rel.From, out var fromNode) || !nodes.TryGetValue(rel.To, out var toNode))
            {
                continue;
            }
            edges.Add(new GraphEdge
            {
                Id = Guid.NewGuid(),
                RepositoryId = repositoryId,
                SnapshotId = snapshotId,
                FromNodeId = fromNode.Id,
                FromKey = rel.From,
                ToNodeId = toNode.Id,
                ToKey = rel.To,
                Type = rel.Type,
                Evidence = rel.Evidence,
                Confidence = rel.Confidence,
                IsStatic = rel.IsStatic
            });
        }

        var rootHash = SemanticHasher.ComputeSnapshotRoot(nodes.Values.Select(n => n.SemanticHash));

        var composed = new ComposedSnapshot
        {
            RootHash = rootHash
        };
        composed.Nodes.AddRange(nodes.Values);
        composed.Edges.AddRange(edges);
        return composed;
    }
}
