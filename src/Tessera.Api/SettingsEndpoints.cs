using Microsoft.EntityFrameworkCore;
using Tessera.Infrastructure.Ai;
using Tessera.Infrastructure.Auth;
using Tessera.Infrastructure.Data;

namespace Tessera.Api;

public static class SettingsEndpoints
{
    public static void MapSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPut("/api/repositories/{repositoryId:guid}/settings", async (
            Guid repositoryId,
            RepoSettingsRequest request,
            HttpContext context,
            TesseraDbContext db,
            CancellationToken ct) =>
        {
            var guarded = await context.GuardRepoAsync(db, repositoryId, ct);
            if (guarded is not null) return guarded;

            var repo = await db.Repositories.FirstOrDefaultAsync(r => r.Id == repositoryId, ct);
            if (repo is null)
            {
                return Results.NotFound(new { error = "Repository not found" });
            }

            repo.EnablePrComments = request.EnablePrComments;
            repo.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Ok(repo);
        });

        app.MapGet("/api/settings/ai", async (
            HttpContext context,
            AiSettingsService settings,
            CancellationToken ct) =>
        {
            var access = context.GetAccess();
            if (access is null)
            {
                return Results.Unauthorized();
            }
            return Results.Ok(await settings.GetAsync(ct));
        });

        app.MapPut("/api/settings/ai", async (
            HttpContext context,
            AiSettingsService settings,
            AiSettingsRequest request,
            CancellationToken ct) =>
        {
            var access = context.GetAccess();
            if (access is null)
            {
                return Results.Unauthorized();
            }
            if (!access.IsAdmin)
            {
                return Results.Json(new { error = "Only administrators can change AI settings." }, statusCode: 403);
            }
            try
            {
                return Results.Ok(await settings.SaveAsync(request, ct));
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        app.MapDelete("/api/settings/ai/{providerName}", async (
            string providerName,
            HttpContext context,
            AiSettingsService settings,
            CancellationToken ct) =>
        {
            var access = context.GetAccess();
            if (access is null)
            {
                return Results.Unauthorized();
            }
            if (!access.IsAdmin)
            {
                return Results.Json(new { error = "Only administrators can change AI settings." }, statusCode: 403);
            }
            await settings.DeleteAsync(providerName, ct);
            return Results.NoContent();
        });

        app.MapPost("/api/settings/ai/{providerName}/primary", async (
            string providerName,
            HttpContext context,
            AiSettingsService settings,
            CancellationToken ct) =>
        {
            var access = context.GetAccess();
            if (access is null)
            {
                return Results.Unauthorized();
            }
            if (!access.IsAdmin)
            {
                return Results.Json(new { error = "Only administrators can change AI settings." }, statusCode: 403);
            }
            try
            {
                await settings.SetPrimaryAsync(providerName, ct);
                return Results.NoContent();
            }
            catch (KeyNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        });
    }
}

public sealed record RepoSettingsRequest(bool EnablePrComments);
