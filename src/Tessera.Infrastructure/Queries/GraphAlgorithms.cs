using Tessera.Domain.Entities;

namespace Tessera.Infrastructure.Queries;

public sealed record DependencyItem(string Key, string Symbol, string Path, int Line, int Count);

// Pure graph computations shared by the API (GraphQueryService) and the offline CLI. Operating on the
// in-memory Domain entities means both surfaces produce identical output for the same snapshot.
public static class GraphAlgorithms
{
    public static IReadOnlyList<ImpactItem> Impact(
        Dictionary<string, KnowledgeNode> nodes,
        List<GraphEdge> edges,
        string entityKey,
        int maxDepth = 10)
    {
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

        return items.OrderBy(i => i.Depth).ThenBy(i => i.Key, StringComparer.Ordinal).ToList();
    }

    public static IReadOnlyList<ConsumerItem> Consumers(
        Dictionary<string, KnowledgeNode> nodes,
        List<GraphEdge> edges,
        string entityKey)
    {
        return edges
            .Where(e => e.ToKey == entityKey)
            .OrderBy(e => e.FromKey, StringComparer.Ordinal)
            .Select(e =>
            {
                nodes.TryGetValue(e.FromKey, out var node);
                var evidence = EvidenceClassifier.ClassifyEdge(e);
                return new ConsumerItem(e.FromKey, node?.Symbol ?? e.FromKey, node?.Path ?? "", node?.StartLine ?? 0, e.Type.ToString(), e.Evidence, e.Confidence, evidence.Classification, evidence.FactSource, evidence.Tier);
            })
            .ToList();
    }

    public static IReadOnlyList<ChainItem> Chain(
        Dictionary<string, KnowledgeNode> nodes,
        List<GraphEdge> edges,
        string entityKey,
        int maxDepth = 10)
    {
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
                var evidence = EvidenceClassifier.ClassifyEdge(edge);
                items.Add(new ChainItem(edge.ToKey, node?.Symbol ?? edge.ToKey, node?.Path ?? "", node?.StartLine ?? 0, depth, edge.Type.ToString(), edge.Evidence, edge.Confidence, evidence.Classification, evidence.FactSource, evidence.Tier));
                queue.Enqueue((edge.ToKey, depth));
            }
        }

        return items.OrderBy(i => i.Depth).ThenBy(i => i.Key, StringComparer.Ordinal).ToList();
    }

    public static IReadOnlyList<DependencyItem> TopDependencies(
        Dictionary<string, KnowledgeNode> nodes,
        List<GraphEdge> edges,
        int count = 20)
    {
        return edges
            .GroupBy(e => e.FromKey, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .Take(count)
            .Select(g =>
            {
                nodes.TryGetValue(g.Key, out var node);
                return new DependencyItem(g.Key, node?.Symbol ?? g.Key, node?.Path ?? "", node?.StartLine ?? 0, g.Count());
            })
            .ToList();
    }

    public static IReadOnlyList<DiffCycle> FindCycles(List<GraphEdge> edges)
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

    private static List<string> RotateToMin(List<string> cycle)
    {
        var body = cycle.Take(cycle.Count - 1).ToList();
        var min = body.Min() ?? "";
        var idx = body.IndexOf(min);
        return [.. body.Skip(idx), .. body.Take(idx), min];
    }
}
