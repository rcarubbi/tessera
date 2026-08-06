using Microsoft.EntityFrameworkCore;
using Tessera.Domain.Entities;
using Tessera.Domain.Enums;
using Tessera.Domain.Merkle;
using Tessera.Infrastructure.Data;
using Tessera.Infrastructure.Queries;

namespace Tessera.Infrastructure.Reviews;

public sealed record ReviewItem(
    Guid NodeId,
    string Key,
    string Symbol,
    string Path,
    int Line,
    int EndLine,
    string Kind,
    double Confidence,
    string ReviewStatus,
    string? Model,
    string? PromptVersion,
    string? Content,
    DateTimeOffset? EditedAt,
    string? EditedBy);

public sealed record ReviewListResult(string CommitSha, IReadOnlyList<ReviewItem> Items);

public sealed class NodeNotFoundException(Guid nodeId) : Exception($"Node '{nodeId}' was not found.");

public sealed class ReviewService(TesseraDbContext db)
{
    public async Task<ReviewListResult> ListAsync(Guid repositoryId, string? commitSha = null, CancellationToken ct = default)
    {
        var snapshot = await db.Snapshots.AsNoTracking()
            .Where(s => s.RepositoryId == repositoryId)
            .Where(s => string.IsNullOrEmpty(commitSha) || s.CommitSha == commitSha)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync(ct)
            ?? throw new SnapshotNotFoundException(repositoryId, commitSha);

        var items = await db.KnowledgeNodes.AsNoTracking()
            .Where(n => n.SnapshotId == snapshot.Id)
            .Where(n => n.ReviewStatus == ReviewStatus.NeedsReview || n.ReviewStatus == ReviewStatus.Stale)
            .Select(n => new ReviewItem(
                n.Id, n.Key, n.Symbol, n.Path, n.StartLine, n.EndLine, n.Kind.ToString(),
                n.Confidence, ReviewStatusLabel.Get(n.ReviewStatus), n.Model, n.PromptVersion,
                n.Content, n.EditedAt, n.EditedBy))
            .ToListAsync(ct);

        return new ReviewListResult(snapshot.CommitSha, items.OrderBy(i => i.Key, StringComparer.Ordinal).ToList());
    }

    public async Task<ReviewItem?> AcceptAsync(Guid repositoryId, Guid nodeId, CancellationToken ct = default)
    {
        var node = await FindAsync(repositoryId, nodeId, ct);
        if (node is null)
        {
            return null;
        }
        node.ReviewStatus = ReviewStatus.Accepted;
        node.EditedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return ToItem(node);
    }

    public async Task<ReviewItem?> DismissAsync(Guid repositoryId, Guid nodeId, CancellationToken ct = default)
    {
        var node = await FindAsync(repositoryId, nodeId, ct);
        if (node is null)
        {
            return null;
        }
        node.ReviewStatus = ReviewStatus.None;
        await db.SaveChangesAsync(ct);
        return ToItem(node);
    }

    public async Task<ReviewItem?> EditAsync(
        Guid repositoryId,
        Guid nodeId,
        string content,
        string? editedBy = null,
        CancellationToken ct = default)
    {
        var node = await FindAsync(repositoryId, nodeId, ct);
        if (node is null)
        {
            return null;
        }
        var edited = content.Trim();
        if (edited.Length == 0)
        {
            throw new ArgumentException("content cannot be empty");
        }

        node.ParentSemanticHash = node.SemanticHash;
        node.SemanticHash = SemanticHasher.Hash(edited);
        node.Content = edited;
        node.ReviewStatus = ReviewStatus.Edited;
        node.EditedAt = DateTimeOffset.UtcNow;
        node.EditedBy = string.IsNullOrWhiteSpace(editedBy) ? "dashboard" : editedBy;
        await db.SaveChangesAsync(ct);
        return ToItem(node);
    }

    private Task<KnowledgeNode?> FindAsync(Guid repositoryId, Guid nodeId, CancellationToken ct) =>
        db.KnowledgeNodes
            .Where(n => n.Id == nodeId && n.RepositoryId == repositoryId)
            .FirstOrDefaultAsync(ct);

    private static ReviewItem ToItem(KnowledgeNode n) => new(
        n.Id, n.Key, n.Symbol, n.Path, n.StartLine, n.EndLine, n.Kind.ToString(),
        n.Confidence, ReviewStatusLabel.Get(n.ReviewStatus), n.Model, n.PromptVersion,
        n.Content, n.EditedAt, n.EditedBy);
}
