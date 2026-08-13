using Microsoft.EntityFrameworkCore;
using Tessera.Infrastructure.Data;
using Tessera.Infrastructure.Queries;
using Tessera.Infrastructure.Reviews;

namespace Tessera.Api;

public static class ReviewEndpoints
{
    public static void MapReviewEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/repositories/{repositoryId:guid}/review", async (
            Guid repositoryId,
            string? commit,
            HttpContext context,
            TesseraDbContext db,
            ReviewService reviews,
            CancellationToken ct) =>
        {
            var guarded = await context.GuardRepoAsync(db, repositoryId, ct);
            if (guarded is not null) return guarded;
            try
            {
                return Results.Ok(await reviews.ListAsync(repositoryId, commit, ct));
            }
            catch (SnapshotNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        });

        app.MapGet("/api/repositories/{repositoryId:guid}/pr-reviews", async (
            Guid repositoryId,
            HttpContext context,
            TesseraDbContext db,
            PrReviewService prReviews,
            CancellationToken ct) =>
        {
            var guarded = await context.GuardRepoAsync(db, repositoryId, ct);
            if (guarded is not null) return guarded;
            return Results.Ok(await prReviews.ListAsync(repositoryId, ct));
        });

        app.MapPost("/api/repositories/{repositoryId:guid}/review/{nodeId:guid}/accept", async (
            Guid repositoryId,
            Guid nodeId,
            HttpContext context,
            TesseraDbContext db,
            ReviewService reviews,
            CancellationToken ct) =>
        {
            var guarded = await context.GuardRepoAsync(db, repositoryId, ct);
            if (guarded is not null) return guarded;
            var item = await reviews.AcceptAsync(repositoryId, nodeId, ct);
            return item is null ? Results.NotFound(new { error = "Node not found." }) : Results.Ok(item);
        });

        app.MapPost("/api/repositories/{repositoryId:guid}/review/{nodeId:guid}/dismiss", async (
            Guid repositoryId,
            Guid nodeId,
            HttpContext context,
            TesseraDbContext db,
            ReviewService reviews,
            CancellationToken ct) =>
        {
            var guarded = await context.GuardRepoAsync(db, repositoryId, ct);
            if (guarded is not null) return guarded;
            var item = await reviews.DismissAsync(repositoryId, nodeId, ct);
            return item is null ? Results.NotFound(new { error = "Node not found." }) : Results.Ok(item);
        });

        app.MapPost("/api/repositories/{repositoryId:guid}/review/{nodeId:guid}/edit", async (
            Guid repositoryId,
            Guid nodeId,
            ReviewEditRequest request,
            HttpContext context,
            TesseraDbContext db,
            ReviewService reviews,
            CancellationToken ct) =>
        {
            var guarded = await context.GuardRepoAsync(db, repositoryId, ct);
            if (guarded is not null) return guarded;
            if (string.IsNullOrWhiteSpace(request.Content))
            {
                return Results.BadRequest(new { error = "content is required" });
            }
            try
            {
                var item = await reviews.EditAsync(repositoryId, nodeId, request.Content, context.GetAccess()?.Login, ct);
                return item is null ? Results.NotFound(new { error = "Node not found." }) : Results.Ok(item);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });
    }
}

public sealed record ReviewEditRequest(string Content, string? EditedBy);
