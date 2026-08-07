using Microsoft.EntityFrameworkCore;
using Tessera.Domain.Entities;
using Tessera.Infrastructure.Data;

namespace Tessera.Infrastructure.Queries;

public sealed record ImpactItem(string Key, string Symbol, string Path, int Line, int Depth, string Severity, string[] Trace);
public sealed record ImpactResult(string Entity, string CommitSha, IReadOnlyList<ImpactItem> Items);

public sealed record ConsumerItem(string FromKey, string FromSymbol, string Path, int Line, string Type, string? Evidence, double Confidence);
public sealed record ConsumerResult(string Entity, string CommitSha, IReadOnlyList<ConsumerItem> Items);

public sealed record ChainItem(string Key, string Symbol, string Path, int Line, int Depth, string Type, string? Evidence, double Confidence);
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
    string? SequenceDiagram);
public sealed record GraphEdgeItem(string From, string To, string Type, string? Evidence, double Confidence, bool IsStatic);
public sealed record GraphResult(string CommitSha, IReadOnlyList<GraphNodeItem> Nodes, IReadOnlyList<GraphEdgeItem> Edges);

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

        var reverseAdj = new Dictionary<string, List<GraphEdge>>(StringComparer.Ordinal);
        foreach (var edge in edges)
        {
            if (!reverseAdj.TryGetValue(edge.ToKey, out var list))
            {
                list = new List<GraphEdge>();
                reverseAdj[edge.ToKey] = list;
            }
            list.Add(edge);
        }

        var items = new List<ImpactItem>();
        var visited = new HashSet<string>(StringComparer.Ordinal) { entityKey };
        var queue = new Queue<(string Key, int Depth, List<string> Trace)>();
        queue.Enqueue((entityKey, 0, [.. new[] { entityKey }]));

        while (queue.TryDequeue(out var current))
        {
            if (current.Depth >= maxDepth || !reverseAdj.TryGetValue(current.Key, out var dependents))
            {
                continue;
            }
            foreach (var edge in dependents.OrderBy(e => e.FromKey, StringComparer.Ordinal))
            {
                if (!visited.Add(edge.FromKey))
                {
                    continue;
                }
                var depth = current.Depth + 1;
                var trace = new List<string>(current.Trace) { edge.FromKey };
                nodes.TryGetValue(edge.FromKey, out var node);
                items.Add(new ImpactItem(
                    edge.FromKey,
                    node?.Symbol ?? edge.FromKey,
                    node?.Path ?? "",
                    node?.StartLine ?? 0,
                    depth,
                    depth == 1 ? "direct" : "indirect",
                    [.. trace]));
                queue.Enqueue((edge.FromKey, depth, trace));
            }
        }

        return new ImpactResult(entityKey, await ResolveCommitAsync(repositoryId, commitSha, ct),
            items.OrderBy(i => i.Depth).ThenBy(i => i.Key, StringComparer.Ordinal).ToList());
    }

    public async Task<ConsumerResult> ConsumersAsync(
        Guid repositoryId,
        string entityKey,
        string? commitSha = null,
        CancellationToken ct = default)
    {
        var (nodes, edges) = await LoadAsync(repositoryId, commitSha, ct);

        var items = edges
            .Where(e => e.ToKey == entityKey)
            .OrderBy(e => e.FromKey, StringComparer.Ordinal)
            .Select(e =>
            {
                nodes.TryGetValue(e.FromKey, out var node);
                return new ConsumerItem(e.FromKey, node?.Symbol ?? e.FromKey, node?.Path ?? "", node?.StartLine ?? 0, e.Type.ToString(), e.Evidence, e.Confidence);
            })
            .ToList();

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

        var adj = new Dictionary<string, List<GraphEdge>>(StringComparer.Ordinal);
        foreach (var edge in edges)
        {
            if (!adj.TryGetValue(edge.FromKey, out var list))
            {
                list = new List<GraphEdge>();
                adj[edge.FromKey] = list;
            }
            list.Add(edge);
        }

        var items = new List<ChainItem>();
        var visited = new HashSet<string>(StringComparer.Ordinal) { entityKey };
        var queue = new Queue<(string Key, int Depth)>();
        queue.Enqueue((entityKey, 0));

        while (queue.TryDequeue(out var current))
        {
            if (current.Depth >= maxDepth || !adj.TryGetValue(current.Key, out var outgoing))
            {
                continue;
            }
            foreach (var edge in outgoing.OrderBy(e => e.ToKey, StringComparer.Ordinal))
            {
                if (!visited.Add(edge.ToKey))
                {
                    continue;
                }
                var depth = current.Depth + 1;
                nodes.TryGetValue(edge.ToKey, out var node);
                items.Add(new ChainItem(edge.ToKey, node?.Symbol ?? edge.ToKey, node?.Path ?? "", node?.StartLine ?? 0, depth, edge.Type.ToString(), edge.Evidence, edge.Confidence));
                queue.Enqueue((edge.ToKey, depth));
            }
        }

        return new ChainResult(entityKey, await ResolveCommitAsync(repositoryId, commitSha, ct),
            items.OrderBy(i => i.Depth).ThenBy(i => i.Key, StringComparer.Ordinal).ToList());
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

    internal static IReadOnlyList<DiffCycle> FindNewCycles(List<GraphEdge> toEdges, HashSet<string> fromEdgeSet)
    {
        var addedEdgePairs = toEdges
            .Where(e => !fromEdgeSet.Contains(EdgeKey(e)))
            .Select(e => $"{e.FromKey}|{e.ToKey}")
            .ToHashSet(StringComparer.Ordinal);
        return FindCycles(toEdges)
            .Where(c => CycleTouchesAddedEdge(c.Path, addedEdgePairs))
            .ToList();
    }

    public async Task<GraphResult> GraphAsync(
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

        var nodeItems = (included is null ? nodes.Values : nodes.Values.Where(n => included.Contains(n.Key)))
            .OrderBy(n => n.Key, StringComparer.Ordinal)
            .Select(n => new GraphNodeItem(
                n.Key, n.Symbol, n.Path, n.Kind.ToString(), n.Language,
                n.StartLine, n.EndLine, n.Confidence, ReviewStatusLabel.Get(n.ReviewStatus),
                n.SemanticHash, n.Content, n.ClassDiagram, n.SequenceDiagram))
            .ToList();

        var edgeItems = edges
            .Where(e => included is null || (included.Contains(e.FromKey) && included.Contains(e.ToKey)))
            .OrderBy(e => e.FromKey, StringComparer.Ordinal)
            .ThenBy(e => e.ToKey, StringComparer.Ordinal)
            .Select(e => new GraphEdgeItem(e.FromKey, e.ToKey, e.Type.ToString(), e.Evidence, e.Confidence, e.IsStatic))
            .ToList();

        return new GraphResult(await ResolveCommitAsync(repositoryId, commitSha, ct), nodeItems, edgeItems);
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

    internal static IReadOnlyList<DiffCycle> FindCycles(List<GraphEdge> edges)
    {
        var adj = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var edge in edges)
        {
            if (!adj.TryGetValue(edge.FromKey, out var list))
            {
                list = new List<string>();
                adj[edge.FromKey] = list;
            }
            list.Add(edge.ToKey);
        }

        var state = new Dictionary<string, int>(StringComparer.Ordinal);
        var stack = new List<string>();
        var cycles = new List<DiffCycle>();

        foreach (var start in adj.Keys)
        {
            if (state.GetValueOrDefault(start) == 0)
            {
                Visit(start);
            }
        }

        return cycles;

        void Visit(string node)
        {
            state[node] = 1;
            stack.Add(node);
            if (adj.TryGetValue(node, out var targets))
            {
                foreach (var target in targets)
                {
                    if (!state.TryGetValue(target, out var targetState))
                    {
                        Visit(target);
                    }
                    else if (targetState == 1)
                    {
                        var start = stack.IndexOf(target);
                        var cycle = stack.Skip(start).Append(target).ToList();
                        var rotated = RotateToMin(cycle);
                        if (cycles.All(c => !c.Path.SequenceEqual(rotated)))
                        {
                            cycles.Add(new DiffCycle(rotated));
                        }
                    }
                }
            }
            stack.RemoveAt(stack.Count - 1);
            state[node] = 2;
        }
    }

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

    private static List<string> RotateToMin(List<string> cycle)
    {
        var body = cycle.Take(cycle.Count - 1).ToList();
        var min = body.Min() ?? "";
        var idx = body.IndexOf(min);
        return [.. body.Skip(idx), .. body.Take(idx), min];
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

    private static string Quote(string key) => $"\"{EscapeLabel(key)}\"";

    private static string EscapeLabel(string value) =>
        value.Replace("\"", "#quot;");
}
