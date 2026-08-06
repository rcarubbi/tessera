using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Tessera.Api;
using Tessera.Domain.Entities;
using Tessera.Infrastructure.Auth;
using Tessera.Infrastructure.Data;

namespace Tessera.Integration.Tests;

public sealed class AccessControlServiceTests
{
    private const string DashboardKey = "dev-key";

    [Fact]
    public async Task Admin_api_key_resolves_to_admin_access()
    {
        using var db = CreateDb();
        var service = new AccessControlService(db, Options.Create(new AuthOptions()));

        var access = await service.AuthenticateAsync($"Bearer {DashboardKey}", DashboardKey);

        Assert.NotNull(access);
        Assert.True(access!.IsAdmin);
        Assert.Equal("admin", access.Login);
    }

    [Fact]
    public async Task Session_resolves_to_user_access_with_installations()
    {
        using var db = CreateDb();
        var user = User("maria", "[1,2]");
        db.GitHubUsers.Add(user);
        await db.SaveChangesAsync();
        var service = new AccessControlService(db, Options.Create(new AuthOptions()));

        var session = await service.CreateSessionAsync(user);
        var access = await service.AuthenticateAsync($"Bearer {session.Token}", "");

        Assert.NotNull(access);
        Assert.False(access!.IsAdmin);
        Assert.Equal("maria", access.Login);
        Assert.Equal(new long[] { 1, 2 }, access.InstallationIds);
    }

    [Fact]
    public async Task Session_beyond_lifetime_is_rejected()
    {
        using var db = CreateDb();
        var user = User("maria", "[]");
        db.GitHubUsers.Add(user);
        db.AuthSessions.Add(new AuthSession
        {
            Id = Guid.NewGuid(),
            Token = "expired-token",
            GitHubUserId = user.Id,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var service = new AccessControlService(db, Options.Create(new AuthOptions()));
        var access = await service.AuthenticateAsync("Bearer expired-token", "");

        Assert.Null(access);
    }

    [Fact]
    public async Task Revoke_invalidates_session()
    {
        using var db = CreateDb();
        var user = User("maria", "[]");
        db.GitHubUsers.Add(user);
        await db.SaveChangesAsync();
        var service = new AccessControlService(db, Options.Create(new AuthOptions()));
        var session = await service.CreateSessionAsync(user);

        await service.RevokeAsync($"Bearer {session.Token}");
        var access = await service.AuthenticateAsync($"Bearer {session.Token}", "");

        Assert.Null(access);
    }

    [Fact]
    public async Task Admin_login_by_config_sees_everything()
    {
        var repo = Repo(installationId: 5);
        var access = new AccessContext(true, "maria", "Maria", "", Array.Empty<long>());

        Assert.True(RepositoryAccess.CanAccess(access, repo));
        Assert.Equal(new[] { repo }, RepositoryAccess.Scope(new[] { repo }, access));
    }

    [Fact]
    public async Task Non_admin_is_scoped_to_own_installations()
    {
        var own = Repo(installationId: 2);
        var other = Repo(installationId: 9);
        var access = new AccessContext(false, "maria", "Maria", "", new long[] { 1, 2 });

        Assert.True(RepositoryAccess.CanAccess(access, own));
        Assert.False(RepositoryAccess.CanAccess(access, other));
        Assert.Equal(new[] { own }, RepositoryAccess.Scope(new[] { own, other }, access));
    }

    [Fact]
    public async Task GuardRepoAsync_denies_outside_installations()
    {
        using var db = CreateDb();
        var repo = Repo(installationId: 9);
        db.Repositories.Add(repo);
        await db.SaveChangesAsync();
        var context = Context(new AccessContext(false, "maria", "Maria", "", new long[] { 1, 2 }));

        var guarded = await context.GuardRepoAsync(db, repo.Id, CancellationToken.None);

        Assert.NotNull(guarded);
        var status = Assert.IsAssignableFrom<IStatusCodeHttpResult>(guarded);
        Assert.Equal(StatusCodes.Status403Forbidden, status.StatusCode);
    }

    [Fact]
    public async Task GuardRepoAsync_allows_own_installations()
    {
        using var db = CreateDb();
        var repo = Repo(installationId: 2);
        db.Repositories.Add(repo);
        await db.SaveChangesAsync();
        var context = Context(new AccessContext(false, "maria", "Maria", "", new long[] { 1, 2 }));

        var guarded = await context.GuardRepoAsync(db, repo.Id, CancellationToken.None);

        Assert.Null(guarded);
    }

    [Fact]
    public async Task GuardRepoAsync_returns_not_found_for_missing_repo()
    {
        using var db = CreateDb();
        var context = Context(new AccessContext(true, "admin", "Admin", "", Array.Empty<long>()));

        var guarded = await context.GuardRepoAsync(db, Guid.NewGuid(), CancellationToken.None);

        Assert.NotNull(guarded);
        var status = Assert.IsAssignableFrom<IStatusCodeHttpResult>(guarded);
        Assert.Equal(StatusCodes.Status404NotFound, status.StatusCode);
    }

    private static GitHubUser User(string login, string installationsJson) => new()
    {
        Id = Guid.NewGuid(),
        Login = login,
        Name = login,
        AvatarUrl = "",
        InstallationIdsJson = installationsJson
    };

    private static Repository Repo(long installationId) => new()
    {
        Id = Guid.NewGuid(),
        FullName = $"owner/repo-{installationId}",
        InstallationId = installationId
    };

    private static DefaultHttpContext Context(AccessContext access)
    {
        var context = new DefaultHttpContext();
        context.Items[AccessControlExtensions.ItemsKey] = access;
        return context;
    }

    private static TesseraDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<TesseraDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new TesseraDbContext(options);
    }
}
