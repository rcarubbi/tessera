using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tessera.Domain.Entities;
using Tessera.Domain.Ports;
using Tessera.Infrastructure.Ai;
using Tessera.Infrastructure.Data;

namespace Tessera.Infrastructure.Analysis;

/// <summary>
/// Backfills node embeddings for a snapshot. Runs in the analysis pipeline so chat
/// retrieval never generates embeddings on the fly.
/// </summary>
public interface IEmbeddingGenerator
{
    Task<int> GenerateAsync(
        Guid snapshotId,
        Guid repositoryId,
        IReadOnlyList<KnowledgeNode> nodes,
        CancellationToken ct = default);
}

public sealed class EmbeddingGenerator(
    TesseraDbContext db,
    IProviderRegistry providers,
    IOptions<AiOptions> options,
    ILogger<EmbeddingGenerator> log) : IEmbeddingGenerator
{
    private readonly AiOptions _options = options.Value;
    private readonly object _throttleLock = new();
    private long _lastCallTicks = Environment.TickCount64;

    private static string EmbeddableText(KnowledgeNode node) =>
        $"{node.Symbol}\n{node.Path} lines {node.StartLine}-{node.EndLine}\n{node.Content}";

    public async Task<int> GenerateAsync(
        Guid snapshotId,
        Guid repositoryId,
        IReadOnlyList<KnowledgeNode> nodes,
        CancellationToken ct = default)
    {
        var provider = providers.Embedding;
        var model = provider?.EmbeddingModel;
        if (provider is null || string.IsNullOrWhiteSpace(model))
        {
            return 0;
        }

        var existing = await db.NodeEmbeddings.AsNoTracking()
            .Where(e => e.SnapshotId == snapshotId && e.Model == model)
            .Select(e => e.NodeId)
            .ToHashSetAsync(ct);

        var missing = nodes.Where(n => !existing.Contains(n.Id)).ToList();
        if (missing.Count == 0)
        {
            return 0;
        }

        var generated = 0;
        var batch = new List<NodeEmbedding>(25);
        foreach (var node in missing)
        {
            ct.ThrowIfCancellationRequested();
            await ThrottleAsync(ct);
            try
            {
                var vector = await provider.EmbedAsync(EmbeddableText(node), ct);
                batch.Add(new NodeEmbedding
                {
                    Id = Guid.NewGuid(),
                    NodeId = node.Id,
                    SnapshotId = snapshotId,
                    RepositoryId = repositoryId,
                    Model = model,
                    Dimensions = vector.Length,
                    Vector = Pack(vector),
                    CreatedAt = DateTimeOffset.UtcNow
                });
                generated++;
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Embedding failed for node {key}; continuing", node.Key);
            }

            if (batch.Count >= 25)
            {
                db.NodeEmbeddings.AddRange(batch);
                await db.SaveChangesAsync(ct);
                batch.Clear();
            }
        }

        if (batch.Count > 0)
        {
            db.NodeEmbeddings.AddRange(batch);
            await db.SaveChangesAsync(ct);
        }

        return generated;
    }

    private async Task ThrottleAsync(CancellationToken ct)
    {
        var rpm = _options.RequestsPerMinute;
        if (rpm <= 0)
        {
            return;
        }

        var minIntervalMs = 60000.0 / rpm;
        long delayMs;
        lock (_throttleLock)
        {
            var now = Environment.TickCount64;
            var next = Math.Max(_lastCallTicks + (long)minIntervalMs, now);
            _lastCallTicks = next;
            delayMs = next - now;
        }

        if (delayMs > 0)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(delayMs), ct);
        }
    }

    private static byte[] Pack(float[] values)
    {
        var bytes = new byte[values.Length * sizeof(float)];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return bytes;
    }
}
