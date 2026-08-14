using Microsoft.EntityFrameworkCore;
using Tessera.Domain.Entities;
using Tessera.Domain.Enums;
using Tessera.Infrastructure.Data;

namespace Tessera.Api;

public static class RepositoryEndpoints
{
    public static void MapRepositoryEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/repositories", async (HttpContext context, TesseraDbContext db) =>
        {
            var access = context.GetAccess();
            var query = db.Repositories.AsNoTracking();
            if (access is not null && !access.IsAdmin)
            {
                query = query.Where(r =>
                    access.InstallationIds.Contains(r.InstallationId)
                    || !string.IsNullOrEmpty(r.CreatedBy) && r.CreatedBy == access.Login);
            }
            return Results.Ok(await query.OrderByDescending(r => r.UpdatedAt).ToListAsync());
        });

        app.MapGet("/api/repositories/{id:guid}", async (Guid id, HttpContext context, TesseraDbContext db) =>
        {
            var guarded = await context.GuardRepoAsync(db, id, context.RequestAborted);
            if (guarded is not null) return guarded;
            var repo = await db.Repositories.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id);
            return repo is null
                ? Results.NotFound(new { error = "Repository not found" })
                : Results.Ok(repo);
        });

        app.MapPost("/api/repositories/local", async (LocalRepositoryRequest? request, HttpContext context, TesseraDbContext db) =>
        {
            var access = context.GetAccess();
            if (access is null)
            {
                return Results.Json(new { error = "Unauthorized." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            var name = request?.Name?.Trim() ?? "";
            if (!LocalRepositoryValidator.IsValidName(name))
            {
                return Results.BadRequest(new { error = "Name must be 1-100 characters and contain only letters, digits, dots, dashes and underscores." });
            }

            var cloneUrl = request?.CloneUrl?.Trim() ?? "";
            if (!LocalRepositoryValidator.IsValidPath(cloneUrl))
            {
                return Results.BadRequest(new { error = "Path must be an absolute path inside the worker (e.g. /repos/local/myapp)." });
            }

            if (await db.Repositories.AnyAsync(r => r.FullName == name))
            {
                return Results.Conflict(new { error = "A repository with this name already exists." });
            }

            var repo = new Repository
            {
                Id = Guid.NewGuid(),
                GitHubId = 0,
                Owner = "local",
                Name = name,
                FullName = name,
                DefaultBranch = string.IsNullOrWhiteSpace(request?.DefaultBranch) ? "main" : request.DefaultBranch.Trim(),
                CloneUrl = cloneUrl,
                InstallationId = 0,
                CreatedBy = access.Login,
                IsConnected = false,
                Status = ProcessingStatus.Pending,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            db.Repositories.Add(repo);
            await db.SaveChangesAsync();
            return Results.Created($"/api/repositories/{repo.Id}", repo);
        });

        app.MapGet("/api/repositories/{id:guid}/snapshots", async (Guid id, HttpContext context, TesseraDbContext db) =>
        {
            var guarded = await context.GuardRepoAsync(db, id, context.RequestAborted);
            if (guarded is not null) return guarded;
            return Results.Ok(await db.Snapshots.AsNoTracking()
                .Where(s => s.RepositoryId == id)
                .OrderByDescending(s => s.CreatedAt)
                .ToListAsync());
        });

        app.MapPost("/api/repositories/{id:guid}/reprocess", async (Guid id, ReprocessRequest? request, HttpContext context, TesseraDbContext db) =>
        {
            var guarded = await context.GuardRepoAsync(db, id, context.RequestAborted);
            if (guarded is not null) return guarded;

            var repo = await db.Repositories.FirstOrDefaultAsync(r => r.Id == id);
            if (repo is null)
            {
                return Results.NotFound(new { error = "Repository not found" });
            }

            var mode = request?.Mode ?? ReprocessMode.Full;
            var includeStatic = request?.IncludeStatic ?? false;
            var includeAi = request?.IncludeAi ?? false;
            var includeIndexing = request?.IncludeIndexing ?? false;

            if (mode == ReprocessMode.Incremental && !includeStatic && !includeAi && !includeIndexing)
            {
                return Results.BadRequest(new { error = "Incremental reprocess requires at least one option (static, AI and/or indexing)." });
            }

            repo.IsConnected = true;
            repo.Status = ProcessingStatus.Pending;
            repo.CancelRequested = false;
            if (mode == ReprocessMode.Full)
            {
                repo.LastProcessedCommit = null;
            }
            repo.ReprocessMode = mode;
            repo.IncludeStaticAnalysis = includeStatic;
            repo.IncludeAiAnalysis = includeAi;
            repo.IncludeIndexing = includeIndexing;
            repo.AnalysisStartedAt = null;
            repo.CompletedAt = null;
            repo.StageStartedAt = null;
            repo.ProcessedCount = 0;
            repo.TotalCount = 0;
            repo.ErrorMessage = null;
            repo.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(repo);
        });

        app.MapPost("/api/repositories/{id:guid}/cancel", async (Guid id, HttpContext context, TesseraDbContext db) =>
        {
            var guarded = await context.GuardRepoAsync(db, id, context.RequestAborted);
            if (guarded is not null) return guarded;

            var repo = await db.Repositories.FirstOrDefaultAsync(r => r.Id == id);
            if (repo is null)
            {
                return Results.NotFound(new { error = "Repository not found" });
            }

            if (repo.Status is ProcessingStatus.Completed
                or ProcessingStatus.Failed or ProcessingStatus.Cancelled)
            {
                return Results.BadRequest(new { error = $"Cannot cancel a repository in state {repo.Status}" });
            }

            if (repo.Status == ProcessingStatus.Pending)
            {
                repo.Status = ProcessingStatus.Cancelled;
                repo.CancelRequested = false;
                repo.StageStartedAt = null;
                repo.UpdatedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync();
                return Results.Ok(repo);
            }

            repo.CancelRequested = true;
            repo.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(repo);
        });
    }
}

public sealed record ReprocessRequest(ReprocessMode Mode, bool IncludeStatic = false, bool IncludeAi = false, bool IncludeIndexing = false);

public sealed record LocalRepositoryRequest(string Name, string CloneUrl, string? DefaultBranch);

public static class LocalRepositoryValidator
{
    private static readonly System.Text.RegularExpressions.Regex NameRegex =
        new("^[A-Za-z0-9._-]{1,100}$", System.Text.RegularExpressions.RegexOptions.Compiled);

    public static bool IsValidName(string name)
        => NameRegex.IsMatch(name);

    public static bool IsValidPath(string path)
        => path.StartsWith('/') && path.Length > 1 && !path.Contains("..");
}
