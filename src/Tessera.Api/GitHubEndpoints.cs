using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tessera.Domain.Entities;
using Tessera.Domain.Enums;
using Tessera.Infrastructure.Data;
using Tessera.Infrastructure.GitHub;

namespace Tessera.Api;

public static class GitHubEndpoints
{
    public static void MapGitHubEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/github/setup", HandleSetupAsync);
        app.MapPost("/api/github/webhook", HandleWebhookAsync);
    }

    private static async Task<IResult> HandleSetupAsync(
        HttpContext context,
        TesseraDbContext db,
        IGitHubAppClient github,
        IOptions<GitHubOptions> options,
        CancellationToken ct)
    {
        if (!IsConfigured(options.Value))
        {
            return Results.Json(new { error = "GitHub integration not configured" }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var installationId = context.Request.Query["installation_id"].ToString();
        var setupAction = context.Request.Query["setup_action"].ToString();

        if (string.IsNullOrEmpty(installationId))
        {
            return Results.BadRequest(new { error = "missing installation_id" });
        }

        if (string.Equals(setupAction, "uninstall", StringComparison.OrdinalIgnoreCase))
        {
            var removed = await db.GitHubInstallations
                .Where(i => i.Id == long.Parse(installationId))
                .ToListAsync(ct);
            foreach (var uninstalled in removed)
            {
                await MarkReposDisconnectedAsync(db, uninstalled.Id, ct);
                db.GitHubInstallations.Remove(uninstalled);
            }
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { status = "uninstalled" });
        }

        var id = long.Parse(installationId);
        var token = await github.CreateInstallationAccessTokenAsync(id, ct);
        var repos = await github.ListInstallationRepositoriesAsync(id, token, ct);

        var install = await db.GitHubInstallations.FindAsync([id], ct);
        if (install is null)
        {
            install = new GitHubInstallation { Id = id, AccountId = id };
            db.GitHubInstallations.Add(install);
        }
        install.AccessToken = token;
        install.TokenExpiresAt = DateTimeOffset.UtcNow.AddHours(1);

        foreach (var repo in repos)
        {
            var existing = await db.Repositories.FirstOrDefaultAsync(r => r.GitHubId == repo.Id, ct);
            if (existing is null)
            {
                db.Repositories.Add(new Repository
                {
                    GitHubId = repo.Id,
                    Owner = repo.Owner,
                    Name = repo.Name,
                    FullName = repo.FullName,
                    CloneUrl = repo.CloneUrl,
                    DefaultBranch = repo.DefaultBranch,
                    InstallationId = id,
                    IsConnected = true,
                    Status = ProcessingStatus.Pending,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                });
            }
            else
            {
                existing.InstallationId = id;
                existing.IsConnected = true;
                existing.CloneUrl = repo.CloneUrl;
                existing.DefaultBranch = repo.DefaultBranch;
                existing.UpdatedAt = DateTimeOffset.UtcNow;
            }
        }

        await db.SaveChangesAsync(ct);

        if (string.IsNullOrEmpty(options.Value.AppUrl))
        {
            return Results.Json(new { status = "installed", installationId = id, repositories = repos.Count });
        }

        return Results.Redirect(options.Value.AppUrl.TrimEnd('/') + "?installed=1");
    }

    private static async Task<IResult> HandleWebhookAsync(
        HttpContext context,
        TesseraDbContext db,
        IOptions<GitHubOptions> options,
        CancellationToken ct)
    {
        if (!IsConfigured(options.Value))
        {
            return Results.Json(new { error = "GitHub integration not configured" }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var eventName = context.Request.Headers["X-GitHub-Event"].ToString();
        var signature = context.Request.Headers["X-Hub-Signature-256"].ToString();
        var bodyBytes = await ReadBodyAsync(context.Request.Body, ct);

        if (!GitHubWebhookSignature.Verify(options.Value.WebhookSecret, bodyBytes, signature))
        {
            return Results.Json(new { error = "invalid signature" }, statusCode: StatusCodes.Status401Unauthorized);
        }

        using var doc = JsonDocument.Parse(bodyBytes);
        var root = doc.RootElement;

        switch (eventName)
        {
            case "ping":
                return Results.Json(new { received = true, type = "ping" });

            case "push":
                await HandlePushAsync(db, root, ct);
                return Results.Json(new { received = true, type = "push" });

            case "installation":
                await HandleInstallationAsync(db, root, ct);
                return Results.Json(new { received = true, type = "installation" });

            case "installation_repositories":
                await HandleInstallationRepositoriesAsync(db, root, ct);
                return Results.Json(new { received = true, type = "installation_repositories" });

            default:
                return Results.Json(new { received = true, type = eventName });
        }
    }

    private static async Task HandlePushAsync(TesseraDbContext db, JsonElement root, CancellationToken ct)
    {
        if (!root.TryGetProperty("repository", out var repoElement) || repoElement.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var gitHubId = repoElement.TryGetProperty("id", out var idElement) && idElement.TryGetInt64(out var id) ? id : 0;
        if (gitHubId == 0)
        {
            return;
        }

        var repo = await db.Repositories.FirstOrDefaultAsync(r => r.GitHubId == gitHubId, ct);
        if (repo is null || !repo.IsConnected)
        {
            return;
        }

        if (TryGetString(repoElement, "full_name", out var fullName) && fullName.Length > 0)
        {
            repo.FullName = fullName;
        }
        if (TryGetString(repoElement, "clone_url", out var cloneUrl) && cloneUrl.Length > 0)
        {
            repo.CloneUrl = cloneUrl;
        }
        if (TryGetString(repoElement, "default_branch", out var defaultBranch) && defaultBranch.Length > 0)
        {
            repo.DefaultBranch = defaultBranch;
        }

        repo.Status = ProcessingStatus.Pending;
        repo.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private static async Task HandleInstallationAsync(TesseraDbContext db, JsonElement root, CancellationToken ct)
    {
        var action = TryGetString(root, "action", out var a) ? a : "";
        var installation = root.TryGetProperty("installation", out var inst) ? inst : default;
        if (installation.ValueKind != JsonValueKind.Object || !installation.TryGetProperty("id", out var idElement) || !idElement.TryGetInt64(out var id))
        {
            return;
        }

        if (action is "removed" or "deleted" or "uninstalled")
        {
            await MarkReposDisconnectedAsync(db, id, ct);
            var installs = await db.GitHubInstallations.Where(i => i.Id == id).ToListAsync(ct);
            db.GitHubInstallations.RemoveRange(installs);
            await db.SaveChangesAsync(ct);
        }
    }

    private static async Task HandleInstallationRepositoriesAsync(TesseraDbContext db, JsonElement root, CancellationToken ct)
    {
        var action = TryGetString(root, "action", out var a) ? a : "";
        var installationId = root.TryGetProperty("installation", out var inst)
            && inst.TryGetProperty("id", out var idElement)
            && idElement.TryGetInt64(out var iid)
            ? iid
            : 0;

        if (installationId == 0 || !root.TryGetProperty("repositories_added", out var added))
        {
            return;
        }

        foreach (var repo in added.EnumerateArray())
        {
            if (!repo.TryGetProperty("id", out var rId) || !rId.TryGetInt64(out var gid))
            {
                continue;
            }

            var existing = await db.Repositories.FirstOrDefaultAsync(r => r.GitHubId == gid, ct);
            if (existing is null)
            {
                db.Repositories.Add(new Repository
                {
                    GitHubId = gid,
                    Owner = TryGetString(repo, "owner", out var o) ? o : "",
                    Name = TryGetString(repo, "name", out var n) ? n : "",
                    FullName = TryGetString(repo, "full_name", out var f) ? f : "",
                    CloneUrl = TryGetString(repo, "clone_url", out var c) ? c : null,
                    DefaultBranch = TryGetString(repo, "default_branch", out var b) ? b : "main",
                    InstallationId = installationId,
                    IsConnected = true,
                    Status = ProcessingStatus.Pending,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                });
            }
            else if (action == "removed")
            {
                existing.IsConnected = false;
                existing.UpdatedAt = DateTimeOffset.UtcNow;
            }
            else
            {
                existing.IsConnected = true;
                existing.InstallationId = installationId;
                existing.Status = ProcessingStatus.Pending;
                existing.UpdatedAt = DateTimeOffset.UtcNow;
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task MarkReposDisconnectedAsync(TesseraDbContext db, long installationId, CancellationToken ct)
    {
        var repos = await db.Repositories.Where(r => r.InstallationId == installationId).ToListAsync(ct);
        foreach (var repo in repos)
        {
            repo.IsConnected = false;
            repo.UpdatedAt = DateTimeOffset.UtcNow;
        }
    }

    private static bool IsConfigured(GitHubOptions options) =>
        !string.IsNullOrEmpty(options.AppId) && !string.IsNullOrEmpty(options.PrivateKeyPath);

    private static async Task<byte[]> ReadBodyAsync(Stream body, CancellationToken ct)
    {
        using var memory = new MemoryStream();
        await body.CopyToAsync(memory, ct);
        return memory.ToArray();
    }

    private static bool TryGetString(JsonElement element, string property, out string value)
    {
        if (element.TryGetProperty(property, out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            value = prop.GetString() ?? "";
            return true;
        }
        value = "";
        return false;
    }
}
