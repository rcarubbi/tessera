using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Tessera.Domain.Merkle;

public sealed record ChildHash(string Key, string EdgeType, string Hash);

public static class SemanticHasher
{
    public static string Compute(string content, IEnumerable<ChildHash> children)
    {
        var orderedChildren = children
            .OrderBy(c => c.Key, StringComparer.Ordinal)
            .ThenBy(c => c.EdgeType, StringComparer.Ordinal)
            .ThenBy(c => c.Hash, StringComparer.Ordinal)
            .Select(c => new[] { c.Key, c.EdgeType, c.Hash })
            .ToList();
        var payload = JsonSerializer.Serialize(new { content, children = orderedChildren });
        return Hash(payload);
    }

    public static string ComputeSnapshotRoot(IEnumerable<string> nodeHashes)
    {
        var payload = string.Join('\n', nodeHashes.OrderBy(h => h, StringComparer.Ordinal));
        return Hash(payload);
    }

    public static string Hash(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var digest = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(digest);
    }

    public static string HashStableJson(object value)
    {
        var json = JsonSerializer.Serialize(value);
        return Hash(json);
    }
}
