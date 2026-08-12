using Microsoft.EntityFrameworkCore;
using Tessera.Infrastructure.Chat;
using Tessera.Infrastructure.Data;
using Tessera.Infrastructure.Queries;

namespace Tessera.Api;

public static class QueryEndpoints
{
    public static void MapQueryEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/repositories/{repositoryId:guid}/impact", async (
            Guid repositoryId,
            string entity,
            string? commit,
            int? maxDepth,
            HttpContext context,
            TesseraDbContext db,
            ImpactAnalysisService impact,
            CancellationToken ct) =>
        {
            var guarded = await context.GuardRepoAsync(db, repositoryId, ct);
            if (guarded is not null) return guarded;
            try
            {
                return Results.Ok(await impact.ReportAsync(repositoryId, entity, commit, maxDepth ?? 10, ct));
            }
            catch (SnapshotNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        });

        app.MapGet("/api/repositories/{repositoryId:guid}/consumers", async (
            Guid repositoryId,
            string entity,
            string? commit,
            HttpContext context,
            TesseraDbContext db,
            GraphQueryService queries,
            CancellationToken ct) =>
        {
            var guarded = await context.GuardRepoAsync(db, repositoryId, ct);
            if (guarded is not null) return guarded;
            try
            {
                return Results.Ok(await queries.ConsumersAsync(repositoryId, entity, commit, ct));
            }
            catch (SnapshotNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        });

        app.MapGet("/api/repositories/{repositoryId:guid}/chain", async (
            Guid repositoryId,
            string entity,
            string? commit,
            int? maxDepth,
            HttpContext context,
            TesseraDbContext db,
            GraphQueryService queries,
            CancellationToken ct) =>
        {
            var guarded = await context.GuardRepoAsync(db, repositoryId, ct);
            if (guarded is not null) return guarded;
            try
            {
                return Results.Ok(await queries.ChainAsync(repositoryId, entity, commit, maxDepth ?? 10, ct));
            }
            catch (SnapshotNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        });

        app.MapGet("/api/repositories/{repositoryId:guid}/diff", async (
            Guid repositoryId,
            string from,
            string to,
            HttpContext context,
            TesseraDbContext db,
            GraphQueryService queries,
            CancellationToken ct) =>
        {
            var guarded = await context.GuardRepoAsync(db, repositoryId, ct);
            if (guarded is not null) return guarded;
            try
            {
                return Results.Ok(await queries.DiffAsync(repositoryId, from, to, ct));
            }
            catch (SnapshotNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        });

        app.MapGet("/api/repositories/{repositoryId:guid}/edge-history", async (
            Guid repositoryId,
            string from,
            string to,
            string? commit,
            HttpContext context,
            TesseraDbContext db,
            GraphQueryService queries,
            CancellationToken ct) =>
        {
            var guarded = await context.GuardRepoAsync(db, repositoryId, ct);
            if (guarded is not null) return guarded;
            try
            {
                return Results.Ok(await queries.EdgeHistoryAsync(repositoryId, from, to, commit, ct));
            }
            catch (SnapshotNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        });

        app.MapGet("/api/repositories/{repositoryId:guid}/edge-changes", async (
            Guid repositoryId,
            string from,
            string to,
            HttpContext context,
            TesseraDbContext db,
            GraphQueryService queries,
            CancellationToken ct) =>
        {
            var guarded = await context.GuardRepoAsync(db, repositoryId, ct);
            if (guarded is not null) return guarded;
            try
            {
                return Results.Ok(await queries.EdgeChangesAsync(repositoryId, from, to, ct));
            }
            catch (SnapshotNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        });

        app.MapGet("/api/repositories/{repositoryId:guid}/overview", async (
            Guid repositoryId,
            HttpContext context,
            TesseraDbContext db,
            CancellationToken ct) =>
        {
            var guarded = await context.GuardRepoAsync(db, repositoryId, ct);
            if (guarded is not null) return guarded;

            var snapshot = await db.Snapshots.AsNoTracking()
                .Where(s => s.RepositoryId == repositoryId)
                .OrderByDescending(s => s.CreatedAt)
                .FirstOrDefaultAsync(ct);
            if (snapshot is null)
            {
                return Results.NotFound(new { error = "Repository has no analyzed snapshot yet." });
            }

            var overview = await db.ProjectOverviews.AsNoTracking()
                .FirstOrDefaultAsync(o => o.SnapshotId == snapshot.Id, ct);
            if (overview is null)
            {
                return Results.NotFound(new { error = "No overview generated for this snapshot yet. Run a new analysis to generate it." });
            }

            return Results.Ok(new
            {
                overview = overview.Content,
                model = overview.Model,
                nodeCount = overview.NodeCount,
                generatedAt = overview.GeneratedAt
            });
        });

        app.MapGet("/api/repositories/{repositoryId:guid}/nodes", async (
            Guid repositoryId,
            string q,
            string? commit,
            int limit,
            HttpContext context,
            TesseraDbContext db,
            CancellationToken ct) =>
        {
            var guarded = await context.GuardRepoAsync(db, repositoryId, ct);
            if (guarded is not null) return guarded;

            var query = db.Snapshots.AsNoTracking()
                .Where(s => s.RepositoryId == repositoryId);
            if (!string.IsNullOrEmpty(commit))
            {
                query = query.Where(s => s.CommitSha == commit);
            }
            var snapshot = await query.OrderByDescending(s => s.CreatedAt).FirstOrDefaultAsync(ct);
            if (snapshot is null)
            {
                return Results.NotFound(new { error = "Repository has no analyzed snapshot yet." });
            }

            var term = q.Trim();
            if (term.Length < 1)
            {
                return Results.Ok(Array.Empty<object>());
            }

            var like = $"%{term}%";
            var nodes = await db.KnowledgeNodes.AsNoTracking()
                .Where(n => n.SnapshotId == snapshot.Id)
                .Where(n =>
                    EF.Functions.ILike(n.Path, like) ||
                    EF.Functions.ILike(n.Key, like) ||
                    EF.Functions.ILike(n.Symbol, like) ||
                    EF.Functions.ILike(n.Kind.ToString(), like))
                .OrderBy(n => n.Path)
                .ThenBy(n => n.StartLine)
                .Take(Math.Clamp(limit, 1, 200))
                .Select(n => new
                {
                    n.Key,
                    n.Symbol,
                    Kind = n.Kind.ToString(),
                    n.Language,
                    n.Path,
                    n.StartLine,
                    n.EndLine,
                    n.Confidence,
                    n.ReviewStatus,
                    n.CommitSha,
                    n.Model,
                    n.PromptVersion,
                    n.AnalyzedAt
                })
                .ToListAsync(ct);
            return Results.Ok(nodes.Select(n =>
            {
                var evidence = EvidenceClassifier.ClassifyNode(n.Model, n.Confidence, n.ReviewStatus);
                return new
                {
                    n.Key,
                    n.Symbol,
                    n.Kind,
                    n.Language,
                    n.Path,
                    StartLine = n.StartLine,
                    EndLine = n.EndLine,
                    n.Confidence,
                    ReviewStatus = n.ReviewStatus.ToString(),
                    n.CommitSha,
                    n.Model,
                    n.PromptVersion,
                    n.AnalyzedAt,
                    evidence.Classification,
                    evidence.FactSource,
                    evidence.Tier
                };
            }));
        });

        app.MapGet("/api/repositories/{repositoryId:guid}/graph", async (
            Guid repositoryId,
            string? entity,
            string? module,
            int? maxDepth,
            string? commit,
            string? source,
            string? tier,
            HttpContext context,
            TesseraDbContext db,
            GraphQueryService queries,
            CancellationToken ct) =>
        {
            var guarded = await context.GuardRepoAsync(db, repositoryId, ct);
            if (guarded is not null) return guarded;
            try
            {
                return Results.Ok(await queries.GraphAsync(repositoryId, entity, module, maxDepth, commit, source, tier, ct));
            }
            catch (SnapshotNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        });

        app.MapGet("/api/repositories/{repositoryId:guid}/graph/mermaid", async (
            Guid repositoryId,
            string? entity,
            string? module,
            int? maxDepth,
            string? commit,
            HttpContext context,
            TesseraDbContext db,
            GraphQueryService queries,
            CancellationToken ct) =>
        {
            var guarded = await context.GuardRepoAsync(db, repositoryId, ct);
            if (guarded is not null) return guarded;
            try
            {
                var mermaid = await queries.MermaidAsync(repositoryId, entity, module, maxDepth, commit, ct);
                return Results.Text(mermaid, "text/vnd.mermaid; charset=utf-8");
            }
            catch (SnapshotNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        });
    }
}
