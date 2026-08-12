using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tessera.Api;
using Tessera.Domain.Entities;
using Tessera.Domain.Enums;
using Tessera.Infrastructure;
using Tessera.Infrastructure.Ai;
using Tessera.Infrastructure.Analysis;
using Tessera.Infrastructure.Auth;
using Tessera.Infrastructure.Chat;
using Tessera.Infrastructure.Data;
using Tessera.Infrastructure.GitHub;
using Tessera.Infrastructure.Queries;
using Tessera.Infrastructure.Reviews;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTesseraInfrastructure(builder.Configuration);
builder.Services.AddHttpClient();
builder.Services.Configure<GitHubOptions>(builder.Configuration.GetSection("GitHub"));
builder.Services.Configure<GitHubOAuthOptions>(builder.Configuration.GetSection("GitHubOAuth"));
builder.Services.Configure<AiOptions>(builder.Configuration.GetSection("Ai"));
builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection("Auth"));
builder.Services.AddSingleton<IGitHubAppClient>(sp =>
    new GitHubAppClient(sp.GetRequiredService<IHttpClientFactory>(), sp.GetRequiredService<IOptions<GitHubOptions>>()));
builder.Services.AddSingleton<IGitClient, GitClient>();
builder.Services.AddSingleton<IGitHubOAuthClient>(sp =>
{
    var options = sp.GetRequiredService<IOptions<GitHubOAuthOptions>>().Value;
    return new GitHubOAuthClient(sp.GetRequiredService<IHttpClientFactory>(), options);
});
builder.Services.AddSingleton<TokenBudgetTracker>();
builder.Services.AddSingleton<AiSettingsCache>();
builder.Services.AddSingleton<ProviderRegistry>();
builder.Services.AddSingleton<IProviderRegistry>(sp => sp.GetRequiredService<ProviderRegistry>());
builder.Services.AddScoped<AiSettingsService>();
builder.Services.AddScoped<GraphQueryService>();
builder.Services.AddScoped<ImpactAnalysisService>();
builder.Services.AddScoped<ArchitectureRuleService>();
builder.Services.AddScoped<AccessControlService>();
builder.Services.AddScoped<IRetrievalService, RetrievalService>();
builder.Services.AddScoped<IArchitectureChatService, ArchitectureChatService>();
builder.Services.AddScoped<ReviewService>();
builder.Services.AddScoped<PrReviewService>();
builder.Services.AddOpenApi();
builder.Services.AddCors(options =>
{
    options.AddPolicy("web", policy =>
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<TesseraDbContext>();
    if (builder.Configuration.GetValue<bool>("MigrateOnStartup", true))
    {
        await db.Database.MigrateAsync();
    }
    await scope.ServiceProvider.GetRequiredService<AiSettingsCache>().RefreshAsync();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseCors("web");

app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value ?? "";
    if (path.StartsWith("/api/", StringComparison.Ordinal)
        && !path.StartsWith("/api/github/", StringComparison.OrdinalIgnoreCase)
        && !path.StartsWith("/api/auth/", StringComparison.OrdinalIgnoreCase))
    {
        var configuration = context.RequestServices.GetRequiredService<IConfiguration>();
        var accessService = context.RequestServices.GetRequiredService<AccessControlService>();
        var access = await accessService.AuthenticateAsync(
            context.Request.Headers.Authorization.ToString(),
            configuration["Dashboard:ApiKey"] ?? "",
            context.RequestAborted);

        if (access is not null)
        {
            context.Items[AccessControlExtensions.ItemsKey] = access;
        }
        else if (AuthRequired(context.RequestServices))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Unauthorized." });
            return;
        }
    }
    await next();
});

static bool AuthRequired(IServiceProvider services)
{
    var configuration = services.GetRequiredService<IConfiguration>();
    if (!string.IsNullOrEmpty(configuration["Dashboard:ApiKey"]))
    {
        return true;
    }
    var oauth = services.GetRequiredService<IOptions<GitHubOAuthOptions>>().Value;
    return !string.IsNullOrEmpty(oauth.ClientId) && !string.IsNullOrEmpty(oauth.ClientSecret);
}

app.MapGet("/health", async (TesseraDbContext db) =>
{
    var canConnect = await db.Database.CanConnectAsync();
    return Results.Ok(new { status = canConnect ? "ok" : "degraded" });
});

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

    if (mode == ReprocessMode.Incremental && !includeStatic && !includeAi)
    {
        return Results.BadRequest(new { error = "Incremental reprocess requires at least one analysis option (static and/or AI)." });
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

app.MapGitHubEndpoints();
app.MapAuthEndpoints();
app.MapQueryEndpoints();
app.MapChatEndpoints();
app.MapReviewEndpoints();
app.MapSettingsEndpoints();
app.MapRuleEndpoints();

app.Run();

public sealed record ReprocessRequest(ReprocessMode Mode, bool IncludeStatic = false, bool IncludeAi = false);

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

public partial class Program { }
