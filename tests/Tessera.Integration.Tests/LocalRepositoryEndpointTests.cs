using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tessera.Domain.Entities;
using Tessera.Domain.Enums;
using Tessera.Infrastructure.Data;

namespace Tessera.Integration.Tests;

public sealed class LocalRepositoryEndpointTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private const string AdminKey = "test-admin-key";

    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly Guid _dbName;

    public LocalRepositoryEndpointTests(WebApplicationFactory<Program> factory)
    {
        _dbName = Guid.NewGuid();

        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("MigrateOnStartup", "false");
            builder.UseSetting("Database:InMemory", "true");
            builder.UseSetting("Database:Name", _dbName.ToString());
            builder.UseSetting("Dashboard:ApiKey", AdminKey);
            builder.UseSetting("Auth:Admins", "");
        });

        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task Request_without_token_is_rejected()
    {
        var response = await _client.PostAsJsonAsync("/api/repositories/local", Payload("myapp", "/repos/local/myapp"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Admin_can_add_local_repository_inactive()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var response = await AdminPostAsync(Payload($"myapp-{suffix}", $"/repos/local/myapp-{suffix}"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var db = CreateDb();
        var repo = await db.Repositories.SingleAsync(r => r.FullName == $"myapp-{suffix}");
        Assert.Equal(0, repo.GitHubId);
        Assert.Equal(0, repo.InstallationId);
        Assert.Equal("local", repo.Owner);
        Assert.False(repo.IsConnected);
        Assert.Equal(ProcessingStatus.Pending, repo.Status);
        Assert.Equal("admin", repo.CreatedBy);
        Assert.Equal("/repos/local/myapp-" + suffix, repo.CloneUrl);
        Assert.Equal("main", repo.DefaultBranch);
    }

    [Fact]
    public async Task Empty_default_branch_falls_back_to_main()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var response = await AdminPostAsync(new { name = $"app-{suffix}", cloneUrl = $"/repos/local/app-{suffix}", defaultBranch = "" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var db = CreateDb();
        var repo = await db.Repositories.SingleAsync(r => r.FullName == $"app-{suffix}");
        Assert.Equal("main", repo.DefaultBranch);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("has/slash")]
    [InlineData("has space")]
    [InlineData("has\\backslash")]
    public async Task Invalid_name_is_rejected(string name)
    {
        var response = await AdminPostAsync(new { name, cloneUrl = "/repos/local/x" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("relative/path")]
    [InlineData("../escape")]
    [InlineData("/")]
    public async Task Invalid_path_is_rejected(string cloneUrl)
    {
        var response = await AdminPostAsync(new { name = "valid", cloneUrl });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Duplicate_full_name_is_conflict()
    {
        var name = "dup-" + Guid.NewGuid().ToString("N")[..6];
        await AdminPostAsync(Payload(name, $"/repos/local/{name}"));
        var response = await AdminPostAsync(Payload(name, $"/repos/local/{name}"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Any_authenticated_user_can_add()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var token = await SeedUserAsync($"alice-{suffix}");

        var response = await PostAsync(token, Payload($"alice-app-{suffix}", $"/repos/local/alice-app-{suffix}"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var db = CreateDb();
        var repo = await db.Repositories.SingleAsync(r => r.FullName == $"alice-app-{suffix}");
        Assert.Equal($"alice-{suffix}", repo.CreatedBy);
    }

    [Fact]
    public async Task Creator_sees_own_local_repository_others_do_not()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var alice = await SeedUserAsync($"alice-{suffix}");
        var bob = await SeedUserAsync($"bob-{suffix}");

        var created = await PostAsync(alice, Payload($"priv-{suffix}", $"/repos/local/priv-{suffix}"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var aliceList = await GetJsonAsync(alice, "/api/repositories");
        Assert.Contains($"priv-{suffix}", aliceList);

        var bobList = await GetJsonAsync(bob, "/api/repositories");
        Assert.DoesNotContain($"priv-{suffix}", bobList);

        var adminList = await GetJsonAsync(AdminKey, "/api/repositories");
        Assert.Contains($"priv-{suffix}", adminList);
    }

    [Fact]
    public async Task Reprocess_activates_inactive_local_repository()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var created = await AdminPostAsync(Payload($"act-{suffix}", $"/repos/local/act-{suffix}"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using var doc = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var id = doc.RootElement.GetProperty("id").GetGuid();

        var response = await AdminPostAsync($"/api/repositories/{id}/reprocess", new { mode = "full" });
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"expected OK, got {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        using var db = CreateDb();
        var repo = await db.Repositories.SingleAsync(r => r.Id == id);
        Assert.True(repo.IsConnected);
        Assert.Equal(ProcessingStatus.Pending, repo.Status);
    }

    [Fact]
    public async Task Non_admin_cannot_activate_another_users_repository()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var alice = await SeedUserAsync($"alice-{suffix}");
        var bob = await SeedUserAsync($"bob-{suffix}");

        var created = await PostAsync(alice, Payload($"own-{suffix}", $"/repos/local/own-{suffix}"));
        var id = JsonDocument.Parse(await created.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();

        var response = await PostAsync(bob, $"/api/repositories/{id}/reprocess", new { mode = "full" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private Task<HttpResponseMessage> AdminPostAsync(object body)
        => PostAsync(AdminKey, body);

    private Task<HttpResponseMessage> AdminPostAsync(string path, object body)
        => PostAsync(AdminKey, path, body);

    private Task<HttpResponseMessage> PostAsync(string token, object body)
        => PostAsync(token, "/api/repositories/local", body);

    private async Task<HttpResponseMessage> PostAsync(string token, string path, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _client.SendAsync(request);
    }

    private async Task<string> GetJsonAsync(string token, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    private async Task<string> SeedUserAsync(string login)
    {
        var token = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        await using var db = CreateDb();
        var user = new GitHubUser { Id = Guid.NewGuid(), Login = login, CreatedAt = DateTimeOffset.UtcNow };
        db.GitHubUsers.Add(user);
        db.AuthSessions.Add(new AuthSession
        {
            Id = Guid.NewGuid(),
            Token = token,
            GitHubUserId = user.Id,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        return token;
    }

    private static object Payload(string name, string cloneUrl)
        => new { name, cloneUrl, defaultBranch = "main" };

    private TesseraDbContext CreateDb() =>
        _factory.Services.CreateScope().ServiceProvider.GetRequiredService<TesseraDbContext>();

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }
}
