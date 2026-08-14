using Microsoft.EntityFrameworkCore;
using Tessera.Domain.Entities;
using Tessera.Domain.Enums;
using Tessera.Infrastructure.Data;
using Tessera.Infrastructure.Queries;

namespace Tessera.Infrastructure.Queries;

public enum ImpactClassification
{
    Test,
    ApiContract,
    DatabaseEntity,
    Other
}

public enum ImpactRating
{
    Low,
    Medium,
    High,
    Critical
}

public sealed record ImpactByType(int Tests, int ApiContracts, int DatabaseEntities, int Other);

public sealed record ClassifiedImpactItem(
    string Key,
    string Symbol,
    string Path,
    int Line,
    int Depth,
    string Severity,
    string[] Trace,
    string Classification,
    string Reason) : ImpactItem(Key, Symbol, Path, Line, Depth, Severity, Trace);

public sealed record ImpactReport(
    string Entity,
    string CommitSha,
    int TotalCount,
    int DirectCount,
    int IndirectCount,
    int MaxDepth,
    ImpactByType ByType,
    string Rating,
    IReadOnlyList<ClassifiedImpactItem> Items);

/// <summary>
/// Productizes the transitive impact query: derives counts, per-type breakdown,
/// deterministic blast-radius rating, and node classification over the existing traversal.
/// </summary>
public sealed class ImpactAnalysisService(TesseraDbContext db, GraphQueryService graph)
{
    public async Task<ImpactReport> ReportAsync(
        Guid repositoryId,
        string entityKey,
        string? commitSha = null,
        int maxDepth = 10,
        CancellationToken ct = default)
    {
        var result = await graph.ImpactAsync(repositoryId, entityKey, commitSha, maxDepth, ct);
        var items = await ClassifyAsync(repositoryId, result.CommitSha, result.Items, ct);

        var total = items.Count;
        var direct = items.Count(i => i.Depth == 1);
        var maxDepthReached = items.Count == 0 ? 0 : items.Max(i => i.Depth);

        return new ImpactReport(
            result.Entity,
            result.CommitSha,
            total,
            direct,
            total - direct,
            maxDepthReached,
            new ImpactByType(
                items.Count(i => i.Classification == ClassificationValue.Test),
                items.Count(i => i.Classification == ClassificationValue.ApiContract),
                items.Count(i => i.Classification == ClassificationValue.DatabaseEntity),
                items.Count(i => i.Classification == ClassificationValue.Other)),
            Rate(direct, total - direct, maxDepthReached),
            items);
    }

    private async Task<IReadOnlyList<ClassifiedImpactItem>> ClassifyAsync(
        Guid repositoryId,
        string commitSha,
        IReadOnlyList<ImpactItem> items,
        CancellationToken ct)
    {
        if (items.Count == 0)
        {
            return Array.Empty<ClassifiedImpactItem>();
        }

        var snapshotId = await db.Snapshots.AsNoTracking()
            .Where(s => s.RepositoryId == repositoryId && s.CommitSha == commitSha)
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => (Guid?)s.Id)
            .FirstOrDefaultAsync(ct);
        if (snapshotId is null)
        {
            return Array.Empty<ClassifiedImpactItem>();
        }

        var nodes = await db.KnowledgeNodes.AsNoTracking()
            .Where(n => n.SnapshotId == snapshotId)
            .ToDictionaryAsync(n => n.Key, StringComparer.Ordinal, ct);
        var edges = await db.GraphEdges.AsNoTracking()
            .Where(e => e.SnapshotId == snapshotId)
            .ToListAsync(ct);

        var apiKeys = edges
            .Where(e => e.Type is EdgeType.InvokesEndpoint or EdgeType.Publishes or EdgeType.Consumes)
            .SelectMany(e => new[] { e.FromKey, e.ToKey })
            .ToHashSet(StringComparer.Ordinal);

        var storageKeys = nodes.Values
            .Where(n => IsStorageConvention(n.Path))
            .Select(n => n.Key)
            .ToHashSet(StringComparer.Ordinal);
        var storageDependentKeys = edges
            .Where(e => e.Type is EdgeType.Injected or EdgeType.FieldDependency
                && (storageKeys.Contains(e.FromKey) || storageKeys.Contains(e.ToKey)))
            .SelectMany(e => new[] { e.FromKey, e.ToKey })
            .ToHashSet(StringComparer.Ordinal);

        return items
            .Select(item =>
            {
                nodes.TryGetValue(item.Key, out var node);
                var (classification, reason) = Classify(item, node, apiKeys, storageDependentKeys);
                return new ClassifiedImpactItem(
                    item.Key, item.Symbol, item.Path, item.Line, item.Depth, item.Severity, item.Trace,
                    ToApiValue(classification), reason);
            })
            .ToList();
    }

    private static (ImpactClassification Classification, string Reason) Classify(
        ImpactItem item,
        KnowledgeNode? node,
        HashSet<string> apiKeys,
        HashSet<string> storageDependentKeys)
    {
        var path = string.IsNullOrWhiteSpace(item.Path) ? node?.Path ?? "" : item.Path;

        if (TestPathDetector.IsTestPath(path))
        {
            return (ImpactClassification.Test, "matched test path convention");
        }

        if (apiKeys.Contains(item.Key))
        {
            return (ImpactClassification.ApiContract, "participates in InvokesEndpoint/Publishes/Consumes edge");
        }

        if (storageDependentKeys.Contains(item.Key))
        {
            return (ImpactClassification.DatabaseEntity, "depends on storage infrastructure via Injected/FieldDependency edge");
        }

        if (IsStorageConvention(path))
        {
            return (ImpactClassification.DatabaseEntity, "matched entity/model/repository path convention");
        }

        return (ImpactClassification.Other, "no convention matched");
    }

    private static bool IsStorageConvention(string path)
    {
        var segments = path.ToLowerInvariant().Split('/');
        return segments.Any(s => s is "entities" or "models" or "repository" or "repositories" or "persistence" or "storage");
    }

    /// <summary>
    /// Deterministic blast-radius rating. Score = 2*direct + indirect + maxDepth.
    /// CRITICAL &gt;= 80 or depth &gt;= 8; HIGH &gt;= 30; MEDIUM &gt;= 8; else LOW.
    /// </summary>
    private static string Rate(int direct, int indirect, int maxDepth)
    {
        var score = 2 * direct + indirect + maxDepth;
        if (score >= 80 || maxDepth >= 8)
        {
            return "CRITICAL";
        }
        if (score >= 30)
        {
            return "HIGH";
        }
        if (score >= 8)
        {
            return "MEDIUM";
        }
        return "LOW";
    }

    private static string ToApiValue(ImpactClassification classification) => classification switch
    {
        ImpactClassification.Test => ClassificationValue.Test,
        ImpactClassification.ApiContract => ClassificationValue.ApiContract,
        ImpactClassification.DatabaseEntity => ClassificationValue.DatabaseEntity,
        _ => ClassificationValue.Other
    };

    private static class ClassificationValue
    {
        public const string Test = "test";
        public const string ApiContract = "api-contract";
        public const string DatabaseEntity = "database-entity";
        public const string Other = "other";
    }
}
