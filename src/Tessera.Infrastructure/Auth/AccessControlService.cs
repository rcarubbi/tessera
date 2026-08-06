using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tessera.Domain.Entities;
using Tessera.Infrastructure.Data;

namespace Tessera.Infrastructure.Auth;

public sealed record AccessContext(
    bool IsAdmin,
    string Login,
    string Name,
    string AvatarUrl,
    IReadOnlyList<long> InstallationIds);

public static class AccessContextExtensions
{
    public static IReadOnlyList<long> ParseInstallationIds(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<long>>(json) ?? new List<long>();
        }
        catch (JsonException)
        {
            return new List<long>();
        }
    }

    public static string SerializeInstallationIds(IReadOnlyList<long> ids)
        => JsonSerializer.Serialize(ids);
}

public sealed class AccessControlService(
    TesseraDbContext db,
    IOptions<AuthOptions> authOptions)
{
    private readonly AuthOptions _auth = authOptions.Value;

    public async Task<AccessContext?> AuthenticateAsync(string? authorizationHeader, string dashboardApiKey, CancellationToken ct = default)
    {
        var token = ExtractBearer(authorizationHeader);
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }

        if (!string.IsNullOrEmpty(dashboardApiKey) && string.Equals(token, dashboardApiKey, StringComparison.Ordinal))
        {
            return new AccessContext(true, "admin", "Administrator", "", Array.Empty<long>());
        }

        var session = await db.AuthSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Token == token, ct);

        if (session is null || session.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            return null;
        }

        var user = await db.GitHubUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == session.GitHubUserId, ct);
        if (user is null)
        {
            return null;
        }

        var isAdmin = _auth.AdminLogins.Contains(user.Login, StringComparer.OrdinalIgnoreCase);
        return new AccessContext(
            isAdmin,
            user.Login,
            user.Name,
            user.AvatarUrl,
            isAdmin ? Array.Empty<long>() : AccessContextExtensions.ParseInstallationIds(user.InstallationIdsJson));
    }

    public async Task<AuthSession> CreateSessionAsync(GitHubUser user, CancellationToken ct = default)
    {
        var session = new AuthSession
        {
            Id = Guid.NewGuid(),
            Token = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
            GitHubUserId = user.Id,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(_auth.SessionLifetimeHours),
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.AuthSessions.Add(session);
        await db.SaveChangesAsync(ct);
        return session;
    }

    public async Task RevokeAsync(string? authorizationHeader, CancellationToken ct = default)
    {
        var token = ExtractBearer(authorizationHeader);
        if (string.IsNullOrEmpty(token))
        {
            return;
        }
        var sessions = await db.AuthSessions.Where(s => s.Token == token).ToListAsync(ct);
        if (sessions.Count > 0)
        {
            db.AuthSessions.RemoveRange(sessions);
            await db.SaveChangesAsync(ct);
        }
    }

    private static string? ExtractBearer(string? header)
    {
        if (string.IsNullOrEmpty(header))
        {
            return null;
        }
        return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? header["Bearer ".Length..].Trim()
            : header.Trim();
    }
}
