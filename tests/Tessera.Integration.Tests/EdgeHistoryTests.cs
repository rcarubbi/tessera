using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Tessera.Domain.Entities;
using Tessera.Domain.Enums;
using Tessera.Infrastructure.Analysis;
using Tessera.Infrastructure.Data;
using Tessera.Infrastructure.Queries;

namespace Tessera.Integration.Tests;

public sealed class EdgeHistoryTests
{
    private static readonly Guid Repo = Guid.NewGuid();

    [Fact]
    public async Task First_snapshot_seeds_history_for_every_edge()
    {
        using var db = CreateDb();
        var s1 = SeedSnapshot(db, "s1", Edge("A", "B"), Edge("B", "C"));

        await ApplyAsync(db, s1.Id, "s1", new[] { Edge("A", "B"), Edge("B", "C") }, null);

        var history = await db.EdgeHistories.Where(h => h.RepositoryId == Repo).ToListAsync();
        Assert.Equal(2, history.Count);
        Assert.All(history, h => Assert.True(h.Live));
        Assert.All(history, h => Assert.Equal("s1", h.IntroducedCommitSha));
        Assert.All(history, h => Assert.Equal(s1.Id, h.IntroducedSnapshotId));
    }

    [Fact]
    public async Task Unchanged_edge_across_snapshots_does_not_duplicate()
    {
        using var db = CreateDb();
        var s1 = SeedSnapshot(db, "s1", Edge("A", "B"), Edge("B", "C"));
        await ApplyAsync(db, s1.Id, "s1", new[] { Edge("A", "B"), Edge("B", "C") }, null);

        var s2 = SeedSnapshot(db, "s2", Edge("A", "B"), Edge("B", "C"));
        await ApplyAsync(db, s2.Id, "s2", new[] { Edge("A", "B"), Edge("B", "C") }, s1.Id);

        var history = await db.EdgeHistories.ToListAsync();
        Assert.Equal(2, history.Count);
        Assert.All(history, h => Assert.True(h.Live));
        var ab = history.Single(h => h.FromKey == "A" && h.ToKey == "B");
        Assert.Equal("s1", ab.IntroducedCommitSha);
        Assert.Equal(s1.Id, ab.IntroducedSnapshotId);
    }

    [Fact]
    public async Task Removed_edge_is_flagged_not_live()
    {
        using var db = CreateDb();
        var s1 = SeedSnapshot(db, "s1", Edge("A", "B"), Edge("B", "C"));
        await ApplyAsync(db, s1.Id, "s1", new[] { Edge("A", "B"), Edge("B", "C") }, null);

        var s2 = SeedSnapshot(db, "s2", Edge("A", "B"));
        await ApplyAsync(db, s2.Id, "s2", new[] { Edge("A", "B") }, s1.Id);

        var kept = db.EdgeHistories.Single(h => h.FromKey == "A" && h.ToKey == "B");
        var removed = db.EdgeHistories.Single(h => h.FromKey == "B" && h.ToKey == "C");
        Assert.True(kept.Live);
        Assert.False(removed.Live);
    }

    [Fact]
    public async Task Reintroduced_edge_creates_new_row()
    {
        using var db = CreateDb();
        var s1 = SeedSnapshot(db, "s1", Edge("A", "B"));
        await ApplyAsync(db, s1.Id, "s1", new[] { Edge("A", "B") }, null);

        var s2 = SeedSnapshot(db, "s2");
        await ApplyAsync(db, s2.Id, "s2", Array.Empty<GraphEdge>(), s1.Id);

        var s3 = SeedSnapshot(db, "s3", Edge("A", "B"));
        await ApplyAsync(db, s3.Id, "s3", new[] { Edge("A", "B") }, s2.Id);

        var rows = await db.EdgeHistories.OrderBy(h => h.IntroducedCommitSha).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal("s1", rows[0].IntroducedCommitSha);
        Assert.False(rows[0].Live);
        Assert.Equal("s3", rows[1].IntroducedCommitSha);
        Assert.True(rows[1].Live);
    }

    [Fact]
    public async Task Edge_history_query_resolves_introducing_commit_and_age()
    {
        using var db = CreateDb();
        var s1 = SeedSnapshot(db, "s1", Edge("A", "B"));
        await ApplyAsync(db, s1.Id, "s1", new[] { Edge("A", "B") }, null);
        var service = new GraphQueryService(db);

        var result = await service.EdgeHistoryAsync(Repo, "A", "B");

        Assert.True(result.Exists);
        Assert.Equal("s1", result.CommitSha);
        var entry = Assert.Single(result.Entries);
        Assert.Equal("s1", entry.IntroducedCommit);
        Assert.Equal(EdgeType.Calls.ToString(), entry.Type);
        Assert.Equal(0, entry.AgeInDays);
    }

    [Fact]
    public async Task Edge_history_query_for_removed_edge_reports_missing_but_keeps_history()
    {
        using var db = CreateDb();
        var s1 = SeedSnapshot(db, "s1", Edge("A", "B"), Edge("B", "C"));
        await ApplyAsync(db, s1.Id, "s1", new[] { Edge("A", "B"), Edge("B", "C") }, null);
        var s2 = SeedSnapshot(db, "s2", Edge("A", "B"));
        await ApplyAsync(db, s2.Id, "s2", new[] { Edge("A", "B") }, s1.Id);
        var service = new GraphQueryService(db);

        var result = await service.EdgeHistoryAsync(Repo, "B", "C");

        Assert.False(result.Exists);
        var entry = Assert.Single(result.Entries);
        Assert.Equal("s1", entry.IntroducedCommit);
    }

    [Fact]
    public async Task Edge_history_query_for_unknown_pair_returns_empty()
    {
        using var db = CreateDb();
        var s1 = SeedSnapshot(db, "s1", Edge("A", "B"));
        await ApplyAsync(db, s1.Id, "s1", new[] { Edge("A", "B") }, null);
        var service = new GraphQueryService(db);

        var result = await service.EdgeHistoryAsync(Repo, "X", "Y");

        Assert.False(result.Exists);
        Assert.Empty(result.Entries);
    }

    [Fact]
    public async Task Edge_changes_reuses_diff_between_commits()
    {
        using var db = CreateDb();
        SeedSnapshot(db, "s1", Edge("A", "B"), Edge("B", "C"));
        SeedSnapshot(db, "s2", Edge("A", "B"), Edge("C", "D"));
        var service = new GraphQueryService(db);

        var changes = await service.EdgeChangesAsync(Repo, "s1", "s2");

        Assert.Equal("s1", changes.FromCommit);
        Assert.Equal("s2", changes.ToCommit);
        Assert.Contains(changes.Edges, e => e.Change == "added" && e.From == "C" && e.To == "D");
        Assert.Contains(changes.Edges, e => e.Change == "removed" && e.From == "B" && e.To == "C");
    }

    private static async Task ApplyAsync(TesseraDbContext db, Guid snapshotId, string sha, GraphEdge[] edges, Guid? previousSnapshotId)
    {
        await EdgeHistoryUpdater.UpdateAsync(db, Repo, snapshotId, sha, edges, previousSnapshotId, CancellationToken.None);
        await db.SaveChangesAsync();
    }

    private static Snapshot SeedSnapshot(TesseraDbContext db, string sha, params GraphEdge[] edges)
    {
        var snapshot = new Snapshot
        {
            Id = Guid.NewGuid(),
            RepositoryId = Repo,
            CommitSha = sha,
            RootHash = $"root-{sha}",
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.Snapshots.Add(snapshot);
        foreach (var edge in edges)
        {
            edge.SnapshotId = snapshot.Id;
            db.GraphEdges.Add(edge);
        }
        db.SaveChanges();
        return snapshot;
    }

    private static GraphEdge Edge(string from, string to) => new()
    {
        Id = Guid.NewGuid(),
        RepositoryId = Repo,
        FromKey = from,
        ToKey = to,
        Type = EdgeType.Calls,
        Confidence = 1.0,
        IsStatic = true
    };

    private static TesseraDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<TesseraDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new TesseraDbContext(options);
    }
}
