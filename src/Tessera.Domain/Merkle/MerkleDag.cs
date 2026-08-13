namespace Tessera.Domain.Merkle;

public sealed record DagNode
{
    public string Key { get; init; } = "";
    public string Content { get; init; } = "";
    public IReadOnlyList<ChildHash> Children { get; init; } = Array.Empty<ChildHash>();
}

public static class MerkleDag
{
    // Bounds fixed-point iteration to the members of an actual cycle, not the whole graph.
    private const int CycleMaxIterations = 10;

    public static IReadOnlyDictionary<string, string> ComputeHashes(
        IEnumerable<DagNode> nodes,
        IReadOnlyDictionary<string, string>? initialOverrides = null)
    {
        var byKey = nodes.ToDictionary(n => n.Key, n => n, StringComparer.Ordinal);
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);

        // Components come out in an order where a node's dependencies are always resolved first.
        foreach (var component in StronglyConnectedComponents.Find(byKey, n => n.Children.Select(c => c.Key)))
        {
            if (component.Count == 1 && !HasSelfLoop(byKey[component[0]]))
            {
                var key = component[0];
                hashes[key] = ResolveHash(byKey[key], hashes, initialOverrides);
            }
            else
            {
                ComputeCycle(component, byKey, hashes, initialOverrides);
            }
        }

        return hashes;
    }

    private static bool HasSelfLoop(DagNode node) =>
        node.Children.Any(c => string.Equals(c.Key, node.Key, StringComparison.Ordinal));

    private static string ResolveHash(
        DagNode node,
        Dictionary<string, string> hashes,
        IReadOnlyDictionary<string, string>? overrides)
    {
        if (overrides != null && overrides.TryGetValue(node.Key, out var overrideHash))
        {
            return overrideHash;
        }
        return SemanticHasher.Compute(node.Content, ResolveChildren(node, hashes));
    }

    // Cycle members can only be resolved with respect to each other, so iterate just this subgraph to a fixed point.
    private static void ComputeCycle(
        IReadOnlyList<string> component,
        Dictionary<string, DagNode> byKey,
        Dictionary<string, string> hashes,
        IReadOnlyDictionary<string, string>? overrides)
    {
        foreach (var key in component)
        {
            hashes[key] = overrides != null && overrides.TryGetValue(key, out var overrideHash)
                ? overrideHash
                : SemanticHasher.Compute(byKey[key].Content, Array.Empty<ChildHash>());
        }

        for (var i = 0; i < CycleMaxIterations; i++)
        {
            var changed = false;
            foreach (var key in component)
            {
                if (overrides != null && overrides.ContainsKey(key))
                {
                    continue;
                }
                var next = SemanticHasher.Compute(byKey[key].Content, ResolveChildren(byKey[key], hashes));
                if (!string.Equals(next, hashes[key], StringComparison.Ordinal))
                {
                    changed = true;
                }
                hashes[key] = next;
            }
            if (!changed)
            {
                break;
            }
        }
    }

    private static IReadOnlyList<ChildHash> ResolveChildren(DagNode node, IReadOnlyDictionary<string, string> hashes)
    {
        return node.Children
            .Where(c => hashes.ContainsKey(c.Key))
            .Select(c => c with { Hash = hashes[c.Key] })
            .ToList();
    }
}
