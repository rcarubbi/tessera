using Microsoft.EntityFrameworkCore;
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
            GraphQueryService queries,
            CancellationToken ct) =>
        {
            var guarded = await context.GuardRepoAsync(db, repositoryId, ct);
            if (guarded is not null) return guarded;
            try
            {
                return Results.Ok(await queries.ImpactAsync(repositoryId, entity, commit, maxDepth ?? 10, ct));
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

        app.MapGet("/api/repositories/{repositoryId:guid}/graph", async (
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
                return Results.Ok(await queries.GraphAsync(repositoryId, entity, module, maxDepth, commit, ct));
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
