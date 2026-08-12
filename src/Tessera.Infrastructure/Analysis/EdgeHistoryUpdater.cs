using Microsoft.EntityFrameworkCore;
using Tessera.Domain.Entities;
using Tessera.Infrastructure.Data;

namespace Tessera.Infrastructure.Analysis;

public static class EdgeHistoryUpdater
{
    public static async Task UpdateAsync(
        TesseraDbContext db,
        Guid repositoryId,
        Guid snapshotId,
        string head,
        IReadOnlyList<GraphEdge> edges,
        Guid? previousSnapshotId,
        CancellationToken ct = default)
    {
        var previousEdgeKeys = previousSnapshotId is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : await LoadEdgeKeysAsync(db, previousSnapshotId.Value, ct);

        var newEdgeKeys = edges.Select(Key).ToHashSet(StringComparer.Ordinal);

        var existing = await db.EdgeHistories
            .Where(h => h.RepositoryId == repositoryId)
            .ToListAsync(ct);
        var liveByKey = existing
            .Where(h => h.Live)
            .ToDictionary(Key, StringComparer.Ordinal);

        var toAdd = new List<EdgeHistory>();
        foreach (var edge in edges)
        {
            var key = Key(edge);
            if (previousEdgeKeys.Contains(key) || liveByKey.ContainsKey(key))
            {
                continue;
            }
            toAdd.Add(new EdgeHistory
            {
                Id = Guid.NewGuid(),
                RepositoryId = repositoryId,
                FromKey = edge.FromKey,
                ToKey = edge.ToKey,
                Type = edge.Type,
                IntroducedSnapshotId = snapshotId,
                IntroducedCommitSha = head,
                IntroducedAt = DateTimeOffset.UtcNow,
                Live = true
            });
        }
        db.EdgeHistories.AddRange(toAdd);

        foreach (var (key, row) in liveByKey)
        {
            if (!newEdgeKeys.Contains(key))
            {
                row.Live = false;
            }
        }
    }

    private static async Task<HashSet<string>> LoadEdgeKeysAsync(TesseraDbContext db, Guid snapshotId, CancellationToken ct)
    {
        var edges = await db.GraphEdges.AsNoTracking()
            .Where(e => e.SnapshotId == snapshotId)
            .Select(e => new { e.FromKey, e.ToKey, e.Type })
            .ToListAsync(ct);
        return edges.Select(e => $"{e.FromKey}|{e.ToKey}|{e.Type}").ToHashSet(StringComparer.Ordinal);
    }

    private static string Key(GraphEdge edge) => $"{edge.FromKey}|{edge.ToKey}|{edge.Type}";

    private static string Key(EdgeHistory history) => $"{history.FromKey}|{history.ToKey}|{history.Type}";
}
