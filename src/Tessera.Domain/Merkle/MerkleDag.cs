namespace Tessera.Domain.Merkle;

public sealed record DagNode
{
    public string Key { get; init; } = "";
    public string Content { get; init; } = "";
    public IReadOnlyList<ChildHash> Children { get; init; } = Array.Empty<ChildHash>();
}

public static class MerkleDag
{
    private const int MaxIterations = 10;

    public static IReadOnlyDictionary<string, string> ComputeHashes(
        IEnumerable<DagNode> nodes,
        IReadOnlyDictionary<string, string>? initialOverrides = null)
    {
        var byKey = nodes.ToDictionary(n => n.Key, n => n);
        var current = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var node in byKey.Values)
        {
            if (initialOverrides != null && initialOverrides.TryGetValue(node.Key, out var overrideHash))
            {
                current[node.Key] = overrideHash;
            }
            else
            {
                current[node.Key] = SemanticHasher.Compute(node.Content, Array.Empty<ChildHash>());
            }
        }

        for (var i = 0; i < MaxIterations; i++)
        {
            var changed = false;
            foreach (var node in byKey.Values)
            {
                if (initialOverrides != null && initialOverrides.ContainsKey(node.Key))
                {
                    continue;
                }
                var next = SemanticHasher.Compute(node.Content, ResolveChildren(node, current));
                if (next != current[node.Key])
                {
                    changed = true;
                }
                current[node.Key] = next;
            }
            if (!changed)
            {
                break;
            }
        }

        return current;
    }

    private static IReadOnlyList<ChildHash> ResolveChildren(DagNode node, IReadOnlyDictionary<string, string> hashes)
    {
        return node.Children
            .Where(c => hashes.ContainsKey(c.Key))
            .Select(c => c with { Hash = hashes[c.Key] })
            .ToList();
    }
}
