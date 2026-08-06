using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Tessera.Domain.Entities;
using Tessera.Domain.Enums;
using Tessera.Domain.Merkle;
using Tessera.Infrastructure.Data;
using Tessera.Infrastructure.Queries;
using Tessera.Infrastructure.Reviews;

namespace Tessera.Integration.Tests;

public sealed class ReviewServiceTests
{
    private static readonly Guid Repo = Guid.NewGuid();

    [Fact]
    public async Task List_returns_needs_review_and_stale_nodes()
    {
        using var db = CreateDb();
        var snapshot = Snapshot("s1");
        db.Snapshots.Add(snapshot);
        db.KnowledgeNodes.AddRange(
            Node(snapshot.Id, "Low", ReviewStatus.NeedsReview, 0.5),
            Node(snapshot.Id, "Stale", ReviewStatus.Stale, 0.9),
            Node(snapshot.Id, "Accepted", ReviewStatus.Accepted, 0.9),
            Node(snapshot.Id, "Ok", ReviewStatus.None, 0.95));
        await db.SaveChangesAsync();

        var result = await new ReviewService(db).ListAsync(Repo);

        Assert.Equal("s1", result.CommitSha);
        Assert.Equal(new[] { "Low", "Stale" }, result.Items.Select(i => i.Symbol).ToArray());
        var low = result.Items.Single(i => i.Key.EndsWith("::Low"));
        Assert.Equal("needs_review", low.ReviewStatus);
        Assert.Contains("# Low", low.Content);
    }

    [Fact]
    public async Task Accept_sets_review_status_accepted()
    {
        using var db = CreateDb();
        var snapshot = Snapshot("s1");
        db.Snapshots.Add(snapshot);
        var node = Node(snapshot.Id, "Low", ReviewStatus.NeedsReview, 0.5);
        db.KnowledgeNodes.Add(node);
        await db.SaveChangesAsync();

        var updated = await new ReviewService(db).AcceptAsync(Repo, node.Id);

        Assert.NotNull(updated);
        Assert.Equal("accepted", updated!.ReviewStatus);
        Assert.NotNull(updated.EditedAt);
    }

    [Fact]
    public async Task Dismiss_sets_review_status_none()
    {
        using var db = CreateDb();
        var snapshot = Snapshot("s1");
        db.Snapshots.Add(snapshot);
        var node = Node(snapshot.Id, "Low", ReviewStatus.NeedsReview, 0.5);
        db.KnowledgeNodes.Add(node);
        await db.SaveChangesAsync();

        var updated = await new ReviewService(db).DismissAsync(Repo, node.Id);

        Assert.Equal("none", updated!.ReviewStatus);
    }

    [Fact]
    public async Task Edit_stores_new_version_with_preserved_provenance()
    {
        using var db = CreateDb();
        var snapshot = Snapshot("s1");
        db.Snapshots.Add(snapshot);
        var node = Node(snapshot.Id, "Order", ReviewStatus.NeedsReview, 0.5);
        db.KnowledgeNodes.Add(node);
        await db.SaveChangesAsync();
        var oldHash = node.SemanticHash;
        var newContent = "# Order\n\n## Responsibilities\n- Human-reviewed responsibilities";

        var updated = await new ReviewService(db).EditAsync(Repo, node.Id, newContent, "maria");

        Assert.Equal("edited", updated!.ReviewStatus);
        Assert.Equal(newContent, updated.Content);
        Assert.Equal("maria", updated.EditedBy);
        Assert.NotNull(updated.EditedAt);
        var persisted = db.KnowledgeNodes.Single(n => n.Id == node.Id);
        Assert.Equal(oldHash, persisted.ParentSemanticHash);
        Assert.Equal(SemanticHasher.Hash(newContent), persisted.SemanticHash);
        Assert.NotEqual(oldHash, persisted.SemanticHash);
    }

    [Fact]
    public async Task Edit_rejects_empty_content()
    {
        using var db = CreateDb();
        var snapshot = Snapshot("s1");
        db.Snapshots.Add(snapshot);
        var node = Node(snapshot.Id, "Order", ReviewStatus.NeedsReview, 0.5);
        db.KnowledgeNodes.Add(node);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            new ReviewService(db).EditAsync(Repo, node.Id, "   "));
    }

    [Fact]
    public async Task Actions_on_missing_node_return_null()
    {
        using var db = CreateDb();
        var service = new ReviewService(db);
        Assert.Null(await service.AcceptAsync(Repo, Guid.NewGuid()));
        Assert.Null(await service.DismissAsync(Repo, Guid.NewGuid()));
    }

    [Fact]
    public async Task List_for_unknown_snapshot_throws()
    {
        using var db = CreateDb();
        await Assert.ThrowsAsync<SnapshotNotFoundException>(() =>
            new ReviewService(db).ListAsync(Repo, "nope"));
    }

    private static Snapshot Snapshot(string sha) => new()
    {
        Id = Guid.NewGuid(), RepositoryId = Repo, CommitSha = sha,
        RootHash = $"root-{sha}", NodeCount = 1, CreatedAt = DateTimeOffset.UtcNow
    };

    private static KnowledgeNode Node(Guid snapshotId, string symbol, ReviewStatus status, double confidence) => new()
    {
        Id = Guid.NewGuid(), RepositoryId = Repo, SnapshotId = snapshotId,
        Key = $"Order.cs::{symbol}", Path = "Order.cs", Symbol = symbol,
        Kind = NodeKind.Class, Language = "csharp", StartLine = 1, EndLine = 10,
        StructuralHash = $"h-{symbol}", SemanticHash = $"s-{symbol}",
        Content = $"# {symbol}", Confidence = confidence, ReviewStatus = status,
        CommitSha = "", AnalyzedAt = DateTimeOffset.UtcNow
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
