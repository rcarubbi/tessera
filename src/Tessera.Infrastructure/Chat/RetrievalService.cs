using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Tessera.Domain.Entities;
using Tessera.Domain.Ports;
using Tessera.Infrastructure.Ai;
using Tessera.Infrastructure.Data;

namespace Tessera.Infrastructure.Chat;

public sealed record RetrievedNode(KnowledgeNode Node, double Score);

public interface IRetrievalService
{
    Task<IReadOnlyList<RetrievedNode>> RetrieveAsync(
        Guid repositoryId,
        string? commitSha,
        string question,
        int topK,
        double threshold,
        CancellationToken ct = default);
}

public sealed class RetrievalService(
    TesseraDbContext db,
    IProviderRegistry providers) : IRetrievalService
{
    private static readonly Regex TokenPattern = new(@"[\p{L}\p{N}_]+", RegexOptions.Compiled);

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "what", "why", "how", "which", "does", "this", "that", "these", "those", "with", "from", "into",
        "they", "them", "you", "your", "are", "was", "were", "has", "have", "had", "will", "would", "can",
        "could", "should", "where", "when", "who", "whose", "and", "but", "for", "not", "one", "all", "any",
        "each", "more", "most", "other", "some", "such", "than", "too", "very", "just", "about", "also", "the",
        "que", "para", "com", "por", "uma", "como", "qual", "quais", "pode", "podem", "ser", "sao", "não",
        "estao", "está", "esta", "ainda", "depois", "antes", "sobre", "entre", "cada", "mesmo", "seu", "sua"
    };

    public async Task<IReadOnlyList<RetrievedNode>> RetrieveAsync(
        Guid repositoryId,
        string? commitSha,
        string question,
        int topK,
        double threshold,
        CancellationToken ct = default)
    {
        var snapshot = await db.Snapshots.AsNoTracking()
            .Where(s => s.RepositoryId == repositoryId)
            .Where(s => string.IsNullOrEmpty(commitSha) || s.CommitSha == commitSha)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (snapshot is null)
        {
            return Array.Empty<RetrievedNode>();
        }

        var tokens = Tokenize(question);
        if (tokens.Count == 0)
        {
            return Array.Empty<RetrievedNode>();
        }

        var nodes = await db.KnowledgeNodes.AsNoTracking()
            .Where(n => n.SnapshotId == snapshot.Id)
            .ToListAsync(ct);

        var embedding = providers.Embedding;
        var scored = embedding is not null
            ? await TryEmbeddingScoreAsync(embedding, snapshot, nodes, question, ct)
            : LexicalScore(nodes, question);

        return scored
            .OrderByDescending(r => r.Score)
            .ThenBy(r => r.Node.Key, StringComparer.Ordinal)
            .Where(r => r.Score >= threshold)
            .Take(topK)
            .ToList();
    }

    private async Task<IReadOnlyList<RetrievedNode>> TryEmbeddingScoreAsync(
        IEmbeddingProvider embedding,
        Snapshot snapshot,
        List<KnowledgeNode> nodes,
        string question,
        CancellationToken ct)
    {
        try
        {
            return await EmbeddingScoreAsync(embedding, snapshot, nodes, question, ct);
        }
        catch (Exception)
        {
            return LexicalScore(nodes, question);
        }
    }

    private async Task<IReadOnlyList<RetrievedNode>> EmbeddingScoreAsync(
        IEmbeddingProvider embedding,
        Snapshot snapshot,
        List<KnowledgeNode> nodes,
        string question,
        CancellationToken ct)
    {
        var model = embedding.EmbeddingModel;
        var cached = await db.NodeEmbeddings.AsNoTracking()
            .Where(e => e.SnapshotId == snapshot.Id && e.Model == model)
            .ToDictionaryAsync(e => e.NodeId, ct);

        // Embeddings are generated in the analysis pipeline. If any node is missing one,
        // fall back to lexical scoring rather than blocking the request on on-the-fly generation.
        if (nodes.Any(n => !cached.ContainsKey(n.Id)))
        {
            return LexicalScore(nodes, question);
        }

        var questionVector = await embedding.EmbedAsync(question, ct);
        var results = new List<RetrievedNode>(nodes.Count);
        foreach (var node in nodes)
        {
            if (!cached.TryGetValue(node.Id, out var entry))
            {
                continue;
            }
            results.Add(new RetrievedNode(node, Cosine(questionVector, Unpack(entry))));
        }
        return results;
    }

    private static IReadOnlyList<RetrievedNode> LexicalScore(List<KnowledgeNode> nodes, string question)
    {
        var tokens = Tokenize(question);
        if (tokens.Count == 0)
        {
            return Array.Empty<RetrievedNode>();
        }

        var results = new List<RetrievedNode>();
        foreach (var node in nodes)
        {
            var text = (node.Symbol + " " + node.Path + " " + node.Content).ToLowerInvariant();
            var matched = tokens.Count(t => text.Contains(t, StringComparison.Ordinal));
            if (matched == 0)
            {
                continue;
            }
            var score = (double)matched / tokens.Count;
            if (tokens.Any(t => node.Symbol.ToLowerInvariant().Contains(t, StringComparison.Ordinal)
                || node.Path.ToLowerInvariant().Contains(t, StringComparison.Ordinal)))
            {
                score = Math.Min(1.0, score + 0.1);
            }
            results.Add(new RetrievedNode(node, score));
        }
        return results;
    }

    private static List<string> Tokenize(string text)
    {
        var tokens = TokenPattern.Matches(text.ToLowerInvariant())
            .Select(m => m.Value)
            .Where(t => t.Length >= 3 && !StopWords.Contains(t));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();
        foreach (var token in tokens)
        {
            if (seen.Add(token))
            {
                result.Add(token);
            }
        }
        return result;
    }

    private static double Cosine(float[] a, float[] b)
    {
        var dot = 0.0;
        var normA = 0.0;
        var normB = 0.0;
        var length = Math.Min(a.Length, b.Length);
        for (var i = 0; i < length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }
        if (normA == 0 || normB == 0)
        {
            return 0;
        }
        return dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }

    private static float[] Unpack(NodeEmbedding entry)
    {
        var values = new float[entry.Vector.Length / sizeof(float)];
        Buffer.BlockCopy(entry.Vector, 0, values, 0, entry.Vector.Length);
        return values;
    }
}
