using Microsoft.EntityFrameworkCore;
using Tessera.Domain.Entities;
using Tessera.Infrastructure.Data;

namespace Tessera.Infrastructure.Queries;

public record ImpactItem(string Key, string Symbol, string Path, int Line, int Depth, string Severity, string[] Trace);
public sealed record ImpactResult(string Entity, string CommitSha, IReadOnlyList<ImpactItem> Items);

public sealed record ConsumerItem(string FromKey, string FromSymbol, string Path, int Line, string Type, string? Evidence, double Confidence, string Classification, string FactSource, string Tier);
public sealed record ConsumerResult(string Entity, string CommitSha, IReadOnlyList<ConsumerItem> Items);

public sealed record ChainItem(string Key, string Symbol, string Path, int Line, int Depth, string Type, string? Evidence, double Confidence, string Classification, string FactSource, string Tier);
public sealed record ChainResult(string Entity, string CommitSha, IReadOnlyList<ChainItem> Items);

public sealed record DiffNodeChange(string Change, string Key, string Symbol, string? Summary);
public sealed record DiffEdgeChange(string Change, string From, string To, string Type);
public sealed record DiffCycle(IReadOnlyList<string> Path);
public sealed record DiffResult(
    string FromCommit,
    string ToCommit,
    IReadOnlyList<DiffNodeChange> Nodes,
    IReadOnlyList<DiffEdgeChange> Edges,
    IReadOnlyList<DiffCycle> Cycles);

public sealed record EdgeHistoryEntry(string Type, string IntroducedCommit, DateTimeOffset IntroducedAt, int AgeInDays);
public sealed record EdgeHistoryResult(string From, string To, bool Exists, string CommitSha, IReadOnlyList<EdgeHistoryEntry> Entries);
public sealed record EdgeChangesResult(string FromCommit, string ToCommit, IReadOnlyList<DiffEdgeChange> Edges);

public sealed record GraphNodeItem(
    string Key,
    string Symbol,
    string Path,
    string Kind,
    string Language,
    int Line,
    int EndLine,
    double Confidence,
    string ReviewStatus,
    string SemanticHash,
    string? Content,
    string? ClassDiagram,
    string? SequenceDiagram,
    string Classification,
    string FactSource,
    string Tier,
    string CommitSha,
    string? Model,
    string? PromptVersion,
    DateTimeOffset AnalyzedAt,
    bool IsTest);
public sealed record GraphEdgeItem(
    string From,
    string To,
    string Type,
    string? Evidence,
    double Confidence,
    bool IsStatic,
    string Classification,
    string FactSource,
    string Tier);
public sealed record GraphResult(string CommitSha, IReadOnlyList<GraphNodeItem> Nodes, IReadOnlyList<GraphEdgeItem> Edges);

public sealed record CriticalComponent(string Key, string Symbol, string Path, int Line, int Centrality);

public sealed class SnapshotNotFoundException(Guid repositoryId, string? commitSha)
    : Exception($"No snapshot for repository {repositoryId} and commit '{commitSha ?? "latest"}'.");

public sealed class GraphQueryService(TesseraDbContext db)
{
    public async Task<ImpactResult> ImpactAsync(
        Guid repositoryId,
        string entityKey,
        string? commitSha = null,
        int maxDepth = 10,
        CancellationToken ct = default)
    {
        var (nodes, edges) = await LoadAsync(repositoryId, commitSha, ct);
        var items = GraphAlgorithms.Impact(nodes, edges, entityKey, maxDepth);
        return new ImpactResult(entityKey, await ResolveCommitAsync(repositoryId, commitSha, ct), items);
    }

    public async Task<ConsumerResult> ConsumersAsync(
        Guid repositoryId,
        string entityKey,
        string? commitSha = null,
        CancellationToken ct = default)
    {
        var (nodes, edges) = await LoadAsync(repositoryId, commitSha, ct);
        var items = GraphAlgorithms.Consumers(nodes, edges, entityKey);
        return new ConsumerResult(entityKey, await ResolveCommitAsync(repositoryId, commitSha, ct), items);
    }

    public async Task<ChainResult> ChainAsync(
        Guid repositoryId,
        string entityKey,
        string? commitSha = null,
        int maxDepth = 10,
        CancellationToken ct = default)
    {
        var (nodes, edges) = await LoadAsync(repositoryId, commitSha, ct);
        var items = GraphAlgorithms.Chain(nodes, edges, entityKey, maxDepth);
        return new ChainResult(entityKey, await ResolveCommitAsync(repositoryId, commitSha, ct), items);
    }

    public async Task<DiffResult> DiffAsync(
        Guid repositoryId,
        string fromCommit,
        string toCommit,
        CancellationToken ct = default)
    {
        var from = await GetSnapshotAsync(repositoryId, fromCommit, ct)
            ?? throw new SnapshotNotFoundException(repositoryId, fromCommit);
        var to = await GetSnapshotAsync(repositoryId, toCommit, ct)
            ?? throw new SnapshotNotFoundException(repositoryId, toCommit);
        var fromNodes = await NodesByKeyAsync(from.Id, ct);
        var toNodes = await NodesByKeyAsync(to.Id, ct);
        var fromEdges = await EdgesAsync(from.Id, ct);
        var toEdges = await EdgesAsync(to.Id, ct);

        var nodeChanges = new List<DiffNodeChange>();

        foreach (var (key, node) in toNodes)
        {
            if (!fromNodes.TryGetValue(key, out var previous))
            {
                nodeChanges.Add(new DiffNodeChange("added", key, node.Symbol, FirstLine(node.Content)));
            }
            else if (previous.SemanticHash != node.SemanticHash)
            {
                nodeChanges.Add(new DiffNodeChange("changed", key, node.Symbol, FirstLine(node.Content)));
            }
        }
        foreach (var (key, node) in fromNodes)
        {
            if (!toNodes.ContainsKey(key))
            {
                nodeChanges.Add(new DiffNodeChange("removed", key, node.Symbol, FirstLine(node.Content)));
            }
        }

        var fromEdgeSet = EdgeKeys(fromEdges);
        var toEdgeSet = EdgeKeys(toEdges);
        var edgeChanges = toEdges
            .Where(e => !fromEdgeSet.Contains(EdgeKey(e)))
            .Select(e => new DiffEdgeChange("added", e.FromKey, e.ToKey, e.Type.ToString()))
            .Concat(fromEdges
                .Where(e => !toEdgeSet.Contains(EdgeKey(e)))
                .Select(e => new DiffEdgeChange("removed", e.FromKey, e.ToKey, e.Type.ToString())))
            .OrderBy(e => e.Change)
            .ThenBy(e => e.From)
            .ThenBy(e => e.To)
            .ToList();

        var cycles = FindNewCycles(toEdges, fromEdgeSet);

        return new DiffResult(fromCommit, toCommit, nodeChanges, edgeChanges, cycles);
    }

    public async Task<EdgeHistoryResult> EdgeHistoryAsync(
        Guid repositoryId,
        string fromKey,
        string toKey,
        string? commitSha = null,
        CancellationToken ct = default)
    {
        var snapshot = await GetSnapshotAsync(repositoryId, commitSha, ct)
            ?? throw new SnapshotNotFoundException(repositoryId, commitSha);

        var exists = await db.GraphEdges.AsNoTracking()
            .AnyAsync(e => e.SnapshotId == snapshot.Id && e.FromKey == fromKey && e.ToKey == toKey, ct);

        var rows = await db.EdgeHistories.AsNoTracking()
            .Where(h => h.RepositoryId == repositoryId && h.FromKey == fromKey && h.ToKey == toKey)
            .OrderBy(h => h.IntroducedAt)
            .ToListAsync(ct);

        IReadOnlyList<EdgeHistory> applicable;
        if (commitSha is null)
        {
            var live = rows.Where(h => h.Live).ToList();
            applicable = live.Count > 0 ? live : rows.TakeLast(1).ToList();
        }
        else
        {
            applicable = rows.Where(h => h.IntroducedAt <= snapshot.CreatedAt).ToList();
        }

        var entries = applicable
            .Select(h => new EdgeHistoryEntry(
                h.Type.ToString(),
                h.IntroducedCommitSha,
                h.IntroducedAt,
                (int)(DateTimeOffset.UtcNow - h.IntroducedAt).TotalDays))
            .ToList();

        return new EdgeHistoryResult(fromKey, toKey, exists, snapshot.CommitSha, entries);
    }

    public async Task<EdgeChangesResult> EdgeChangesAsync(
        Guid repositoryId,
        string fromSha,
        string toSha,
        CancellationToken ct = default)
    {
        var diff = await DiffAsync(repositoryId, fromSha, toSha, ct);
        return new EdgeChangesResult(diff.FromCommit, diff.ToCommit, diff.Edges);
    }

    internal static IReadOnlyList<DiffCycle> FindNewCycles(List<GraphEdge> toEdges, HashSet<string> fromEdgeSet)
    {
        var addedEdgePairs = toEdges
            .Where(e => !fromEdgeSet.Contains(EdgeKey(e)))
            .Select(e => $"{e.FromKey}|{e.ToKey}")
            .ToHashSet(StringComparer.Ordinal);
        return GraphAlgorithms.FindCycles(toEdges)
            .Where(c => CycleTouchesAddedEdge(c.Path, addedEdgePairs))
            .ToList();
    }

    public async Task<GraphResult> GraphAsync(
        Guid repositoryId,
        string? entityKey = null,
        string? module = null,
        int? maxDepth = null,
        string? commitSha = null,
        string? source = null,
        string? tier = null,
        CancellationToken ct = default)
    {
        var (nodes, edges) = await LoadAsync(repositoryId, commitSha, ct);

        HashSet<string>? included = null;
        if (!string.IsNullOrEmpty(entityKey))
        {
            included = BfsSubgraph(edges, entityKey, maxDepth ?? 3);
        }
        else if (!string.IsNullOrEmpty(module))
        {
            included = nodes.Values
                .Where(n => n.Path.StartsWith(module, StringComparison.OrdinalIgnoreCase))
                .Select(n => n.Key)
                .ToHashSet(StringComparer.Ordinal);
        }

        var nodeItems = (included is null ? nodes.Values : nodes.Values.Where(n => included.Contains(n.Key)))
            .Where(n => MatchesFilter(EvidenceClassifier.ClassifyNode(n), source, tier))
            .OrderBy(n => n.Key, StringComparer.Ordinal)
            .Select(n =>
            {
                var evidence = EvidenceClassifier.ClassifyNode(n);
                return new GraphNodeItem(
                    n.Key, n.Symbol, n.Path, n.Kind.ToString(), n.Language,
                    n.StartLine, n.EndLine, n.Confidence, ReviewStatusLabel.Get(n.ReviewStatus),
                    n.SemanticHash, n.Content, n.ClassDiagram, n.SequenceDiagram,
                    evidence.Classification, evidence.FactSource, evidence.Tier,
                    n.CommitSha, n.Model, n.PromptVersion, n.AnalyzedAt,
                    TestPathDetector.IsTestPath(n.Path));
            })
            .ToList();

        var edgeItems = edges
            .Where(e => included is null || (included.Contains(e.FromKey) && included.Contains(e.ToKey)))
            .Where(e => MatchesFilter(EvidenceClassifier.ClassifyEdge(e), source, tier))
            .OrderBy(e => e.FromKey, StringComparer.Ordinal)
            .ThenBy(e => e.ToKey, StringComparer.Ordinal)
            .Select(e =>
            {
                var evidence = EvidenceClassifier.ClassifyEdge(e);
                return new GraphEdgeItem(e.FromKey, e.ToKey, e.Type.ToString(), e.Evidence, e.Confidence, e.IsStatic, evidence.Classification, evidence.FactSource, evidence.Tier);
            })
            .ToList();

        return new GraphResult(await ResolveCommitAsync(repositoryId, commitSha, ct), nodeItems, edgeItems);
    }

    public async Task<IReadOnlyList<CriticalComponent>> TopByDegreeAsync(
        Guid repositoryId,
        string? commitSha = null,
        int top = 10,
        CancellationToken ct = default)
    {
        var (nodes, edges) = await LoadAsync(repositoryId, commitSha, ct);
        var degree = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var edge in edges)
        {
            if (nodes.ContainsKey(edge.FromKey))
            {
                degree[edge.FromKey] = degree.GetValueOrDefault(edge.FromKey) + 1;
            }
            if (nodes.ContainsKey(edge.ToKey))
            {
                degree[edge.ToKey] = degree.GetValueOrDefault(edge.ToKey) + 1;
            }
        }

        return nodes.Values
            .Select(n => new CriticalComponent(n.Key, n.Symbol, n.Path, n.StartLine, degree.GetValueOrDefault(n.Key)))
            .Where(c => c.Centrality > 0)
            .OrderByDescending(c => c.Centrality)
            .ThenBy(c => c.Key, StringComparer.Ordinal)
            .Take(top)
            .ToList();
    }

    public async Task<string> MermaidAsync(
        Guid repositoryId,
        string? entityKey = null,
        string? module = null,
        int? maxDepth = null,
        string? commitSha = null,
        CancellationToken ct = default)
    {
        var (nodes, edges) = await LoadAsync(repositoryId, commitSha, ct);

        HashSet<string>? included = null;
        if (!string.IsNullOrEmpty(entityKey))
        {
            included = BfsSubgraph(edges, entityKey, maxDepth ?? 3);
        }
        else if (!string.IsNullOrEmpty(module))
        {
            included = nodes.Values
                .Where(n => n.Path.StartsWith(module, StringComparison.OrdinalIgnoreCase))
                .Select(n => n.Key)
                .ToHashSet(StringComparer.Ordinal);
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("flowchart LR");

        var selectedNodes = included is null
            ? nodes.Values
            : nodes.Values.Where(n => included.Contains(n.Key));
        foreach (var node in selectedNodes.OrderBy(n => n.Key, StringComparer.Ordinal))
        {
            sb.AppendLine($"  {Quote(node.Key)}[\"{EscapeLabel(node.Symbol)}<br/>{EscapeLabel(node.Path)}\"]");
        }

        var selectedEdges = edges.Where(e =>
            included is null || (included.Contains(e.FromKey) && included.Contains(e.ToKey)));
        foreach (var edge in selectedEdges.OrderBy(e => e.FromKey, StringComparer.Ordinal).ThenBy(e => e.ToKey, StringComparer.Ordinal))
        {
            sb.AppendLine($"  {Quote(edge.FromKey)} -->|{EscapeLabel(edge.Type.ToString())}| {Quote(edge.ToKey)}");
        }

        return sb.ToString();
    }

    private static HashSet<string> BfsSubgraph(List<GraphEdge> edges, string root, int maxDepth)
    {
        var adj = new Dictionary<string, List<GraphEdge>>(StringComparer.Ordinal);
        var reverseAdj = new Dictionary<string, List<GraphEdge>>(StringComparer.Ordinal);
        foreach (var edge in edges)
        {
            Add(adj, edge.FromKey, edge);
            Add(reverseAdj, edge.ToKey, edge);
        }

        var visited = new HashSet<string>(StringComparer.Ordinal) { root };
        var queue = new Queue<(string Key, int Depth)>();
        queue.Enqueue((root, 0));

        while (queue.TryDequeue(out var current))
        {
            if (current.Depth >= maxDepth)
            {
                continue;
            }
            if (adj.TryGetValue(current.Key, out var outgoing))
            {
                foreach (var edge in outgoing)
                {
                    if (visited.Add(edge.ToKey))
                    {
                        queue.Enqueue((edge.ToKey, current.Depth + 1));
                    }
                }
            }
            if (reverseAdj.TryGetValue(current.Key, out var incoming))
            {
                foreach (var edge in incoming)
                {
                    if (visited.Add(edge.FromKey))
                    {
                        queue.Enqueue((edge.FromKey, current.Depth + 1));
                    }
                }
            }
        }
        return visited;

        static void Add(Dictionary<string, List<GraphEdge>> map, string key, GraphEdge edge)
        {
            if (!map.TryGetValue(key, out var list))
            {
                list = new List<GraphEdge>();
                map[key] = list;
            }
            list.Add(edge);
        }
    }

    internal static IReadOnlyList<DiffCycle> FindCycles(List<GraphEdge> edges) => GraphAlgorithms.FindCycles(edges);

    private static bool CycleTouchesAddedEdge(IReadOnlyList<string> path, HashSet<string> addedEdgePairs)
    {
        for (var i = 0; i < path.Count - 1; i++)
        {
            if (addedEdgePairs.Contains($"{path[i]}|{path[i + 1]}"))
            {
                return true;
            }
        }
        return false;
    }

    private async Task<(Dictionary<string, KnowledgeNode> Nodes, List<GraphEdge> Edges)> LoadAsync(
        Guid repositoryId,
        string? commitSha,
        CancellationToken ct)
    {
        var snapshot = await GetSnapshotAsync(repositoryId, commitSha, ct)
            ?? throw new SnapshotNotFoundException(repositoryId, commitSha);
        var nodes = await NodesByKeyAsync(snapshot.Id, ct);
        var edges = await EdgesAsync(snapshot.Id, ct);
        return (nodes, edges);
    }

    private async Task<string> ResolveCommitAsync(Guid repositoryId, string? commitSha, CancellationToken ct)
    {
        var snapshot = await GetSnapshotAsync(repositoryId, commitSha, ct)
            ?? throw new SnapshotNotFoundException(repositoryId, commitSha);
        return snapshot.CommitSha;
    }

    private Task<Snapshot?> GetSnapshotAsync(Guid repositoryId, string? commitSha, CancellationToken ct)
    {
        var query = db.Snapshots.Where(s => s.RepositoryId == repositoryId);
        if (!string.IsNullOrEmpty(commitSha))
        {
            query = query.Where(s => s.CommitSha == commitSha);
        }
        return query.OrderByDescending(s => s.CreatedAt).FirstOrDefaultAsync(ct);
    }

    private Task<Dictionary<string, KnowledgeNode>> NodesByKeyAsync(Guid snapshotId, CancellationToken ct) =>
        db.KnowledgeNodes.AsNoTracking()
            .Where(n => n.SnapshotId == snapshotId)
            .ToDictionaryAsync(n => n.Key, StringComparer.Ordinal, ct);

    private Task<List<GraphEdge>> EdgesAsync(Guid snapshotId, CancellationToken ct) =>
        db.GraphEdges.AsNoTracking()
            .Where(e => e.SnapshotId == snapshotId)
            .ToListAsync(ct);

    private static string EdgeKey(GraphEdge edge) => $"{edge.FromKey}|{edge.ToKey}|{edge.Type}";

    private static HashSet<string> EdgeKeys(List<GraphEdge> edges) =>
        edges.Select(EdgeKey).ToHashSet(StringComparer.Ordinal);

    private static string? FirstLine(string? content) =>
        content?.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();

    private static bool MatchesFilter(EvidenceClassification evidence, string? source, string? tier)
    {
        if (!string.IsNullOrEmpty(source))
        {
            var sourceMatches = source switch
            {
                "facts" => evidence.Classification == "fact",
                "inferences" => evidence.Classification == "inference",
                _ => true
            };
            if (!sourceMatches)
            {
                return false;
            }
        }
        if (!string.IsNullOrEmpty(tier)
            && !string.Equals(evidence.Tier, tier, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        return true;
    }

    private static string Quote(string key) => $"\"{EscapeLabel(key)}\"";

    private static string EscapeLabel(string value) =>
        value.Replace("\"", "#quot;");
}
