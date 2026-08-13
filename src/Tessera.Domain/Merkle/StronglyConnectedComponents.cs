namespace Tessera.Domain.Merkle;

// Iterative Tarjan's algorithm: avoids recursion so deep dependency chains cannot overflow the stack,
// and yields components in an order where every edge target is emitted before the node that references it.
internal static class StronglyConnectedComponents
{
    public static IReadOnlyList<IReadOnlyList<string>> Find<TNode>(
        IReadOnlyDictionary<string, TNode> byKey,
        Func<TNode, IEnumerable<string>> children)
    {
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        var lowLink = new Dictionary<string, int>(StringComparer.Ordinal);
        var onStack = new HashSet<string>(StringComparer.Ordinal);
        var sccStack = new Stack<string>();
        var components = new List<IReadOnlyList<string>>();
        var nextIndex = 0;

        IEnumerable<string> Neighbors(string key) =>
            byKey.TryGetValue(key, out var node) ? children(node).Where(byKey.ContainsKey) : [];

        // Deterministic traversal order for the same input set, independent of dictionary enumeration order.
        foreach (var key in byKey.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            if (index.ContainsKey(key))
            {
                continue;
            }

            var work = new Stack<(string Node, IEnumerator<string> Children)>();
            Push(key);

            while (work.Count > 0)
            {
                var (node, iterator) = work.Peek();
                if (iterator.MoveNext())
                {
                    var child = iterator.Current;
                    if (!index.ContainsKey(child))
                    {
                        Push(child);
                    }
                    else if (onStack.Contains(child))
                    {
                        lowLink[node] = Math.Min(lowLink[node], index[child]);
                    }
                }
                else
                {
                    work.Pop();
                    if (work.Count > 0)
                    {
                        var parent = work.Peek().Node;
                        lowLink[parent] = Math.Min(lowLink[parent], lowLink[node]);
                    }

                    if (lowLink[node] == index[node])
                    {
                        var component = new List<string>();
                        string member;
                        do
                        {
                            member = sccStack.Pop();
                            onStack.Remove(member);
                            component.Add(member);
                        } while (!string.Equals(member, node, StringComparison.Ordinal));
                        components.Add(component);
                    }
                }
            }

            void Push(string node)
            {
                index[node] = nextIndex;
                lowLink[node] = nextIndex;
                nextIndex++;
                sccStack.Push(node);
                onStack.Add(node);
                work.Push((node, Neighbors(node).GetEnumerator()));
            }
        }

        return components;
    }
}
