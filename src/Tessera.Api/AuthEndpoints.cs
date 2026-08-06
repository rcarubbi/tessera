using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tessera.Domain.Entities;
using Tessera.Infrastructure.Auth;
using Tessera.Infrastructure.Data;
using Tessera.Infrastructure.GitHub;

namespace Tessera.Api;

public static class AccessControlExtensions
{
    public const string ItemsKey = "Tessera.Access";

    public static AccessContext? GetAccess(this HttpContext context)
        => context.Items.TryGetValue(ItemsKey, out var value) ? value as AccessContext : null;

    public static async Task<IResult?> GuardRepoAsync(this HttpContext context, TesseraDbContext db, Guid repositoryId, CancellationToken ct)
    {
        var repo = await db.Repositories.AsNoTracking().FirstOrDefaultAsync(r => r.Id == repositoryId, ct);
        if (repo is null)
        {
            return Results.NotFound(new { error = "Repository not found" });
        }
        return Guard(context, repo);
    }

    public static IResult? Guard(this HttpContext context, Repository repo)
    {
        var access = context.GetAccess();
        if (access is null)
        {
            return Results.Json(new { error = "Unauthorized." }, statusCode: StatusCodes.Status401Unauthorized);
        }
        if (!RepositoryAccess.CanAccess(access, repo))
        {
            return Results.Json(new { error = "Forbidden." }, statusCode: StatusCodes.Status403Forbidden);
        }
        return null;
    }
}

public static class AuthEndpoints
{
    private const string StateCookieName = "tessera.oauth_state";

    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/auth/login", HandleLoginAsync);
        app.MapGet("/api/auth/callback", HandleCallbackAsync);
        app.MapPost("/api/auth/logout", HandleLogoutAsync);
        app.MapGet("/api/auth/me", HandleMeAsync);
        app.MapGet("/api/auth/config", (IOptions<GitHubOAuthOptions> options) =>
            Results.Ok(new { githubEnabled = IsConfigured(options.Value) }));
    }

    private static IResult HandleLoginAsync(HttpContext context, IOptions<GitHubOAuthOptions> options, IGitHubOAuthClient oauth)
    {
        if (!IsConfigured(options.Value))
        {
            return Results.Json(new { error = "GitHub OAuth not configured" }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var state = Guid.NewGuid().ToString("N");
        context.Response.Cookies.Append(StateCookieName, state, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            MaxAge = TimeSpan.FromMinutes(10)
        });

        return Results.Redirect(oauth.BuildAuthorizeUrl(state));
    }

    private static async Task<IResult> HandleCallbackAsync(
        HttpContext context,
        TesseraDbContext db,
        AccessControlService accessService,
        IGitHubOAuthClient oauth,
        IOptions<GitHubOAuthOptions> options,
        CancellationToken ct)
    {
        var expectedState = context.Request.Cookies[StateCookieName];
        context.Response.Cookies.Delete(StateCookieName);

        var code = context.Request.Query["code"].ToString();
        var state = context.Request.Query["state"].ToString();
        if (string.IsNullOrEmpty(code)
            || string.IsNullOrEmpty(expectedState)
            || !string.Equals(state, expectedState, StringComparison.Ordinal)
            || !IsConfigured(options.Value))
        {
            return RedirectToWeb(options.Value, "error=oauth_failed");
        }

        string accessToken;
        GitHubOAuthUser githubUser;
        IReadOnlyList<long> installations;
        try
        {
            accessToken = await oauth.ExchangeCodeAsync(code, ct);
            githubUser = await oauth.GetUserAsync(accessToken, ct);
            installations = await oauth.GetUserInstallationsAsync(accessToken, ct);
        }
        catch (Exception)
        {
            return RedirectToWeb(options.Value, "error=oauth_failed");
        }

        var user = await db.GitHubUsers.FirstOrDefaultAsync(u => u.Login == githubUser.Login, ct);
        if (user is null)
        {
            user = new GitHubUser { Id = Guid.NewGuid(), Login = githubUser.Login, CreatedAt = DateTimeOffset.UtcNow };
            db.GitHubUsers.Add(user);
        }
        user.Name = githubUser.Name;
        user.AvatarUrl = githubUser.AvatarUrl;
        user.InstallationIdsJson = AccessContextExtensions.SerializeInstallationIds(installations);
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        var session = await accessService.CreateSessionAsync(user, ct);
        return RedirectToWeb(options.Value, $"token={session.Token}");
    }

    private static async Task<IResult> HandleLogoutAsync(HttpContext context, AccessControlService accessService, CancellationToken ct)
    {
        await accessService.RevokeAsync(context.Request.Headers.Authorization.ToString(), ct);
        return Results.Ok(new { status = "signed_out" });
    }

    private static async Task<IResult> HandleMeAsync(
        HttpContext context,
        AccessControlService accessService,
        IConfiguration configuration,
        CancellationToken ct)
    {
        var dashboardApiKey = configuration["Dashboard:ApiKey"] ?? "";
        var access = await accessService.AuthenticateAsync(context.Request.Headers.Authorization.ToString(), dashboardApiKey, ct);
        if (access is null)
        {
            return Results.Json(new { error = "Unauthorized." }, statusCode: StatusCodes.Status401Unauthorized);
        }
        return Results.Ok(new
        {
            login = access.Login,
            name = access.Name,
            avatarUrl = access.AvatarUrl,
            isAdmin = access.IsAdmin
        });
    }

    private static IResult RedirectToWeb(GitHubOAuthOptions options, string query)
        => Results.Redirect($"{options.WebUrl.TrimEnd('/')}/repos?{query}");

    private static bool IsConfigured(GitHubOAuthOptions options)
        => !string.IsNullOrEmpty(options.ClientId) && !string.IsNullOrEmpty(options.ClientSecret);
}
