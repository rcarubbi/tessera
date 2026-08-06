using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Tessera.Domain.Merkle;

public sealed record ChildHash(string Key, string EdgeType, string Hash);

public static class SemanticHasher
{
    public static string Compute(string content, IEnumerable<ChildHash> children)
    {
        var payload = new StringBuilder(content);
        foreach (var child in children.OrderBy(c => c.Key, StringComparer.Ordinal))
        {
            payload.Append('\n').Append(child.Key).Append('|').Append(child.EdgeType).Append('|').Append(child.Hash);
        }
        return Hash(payload.ToString());
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

    public static string StableJson(object value)
    {
        var json = JsonSerializer.Serialize(value);
        return Hash(json);
    }
}
