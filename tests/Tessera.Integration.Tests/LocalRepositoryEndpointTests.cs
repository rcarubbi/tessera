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
    public async Task Admin_can_add_local_repository_connected_for_automatic_processing()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var response = await AdminPostAsync(Payload($"myapp-{suffix}", $"/repos/local/myapp-{suffix}"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var db = CreateDb();
        var repo = await db.Repositories.SingleAsync(r => r.FullName == $"myapp-{suffix}");
        Assert.Equal(0, repo.GitHubId);
        Assert.Equal(0, repo.InstallationId);
        Assert.Equal("local", repo.Owner);
        Assert.True(repo.IsConnected);
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

    [Fact]
    public async Task Edge_history_requires_access_to_repository()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var alice = await SeedUserAsync($"alice-{suffix}");
        var bob = await SeedUserAsync($"bob-{suffix}");

        var created = await PostAsync(alice, Payload($"eh-{suffix}", $"/repos/local/eh-{suffix}"));
        var id = JsonDocument.Parse(await created.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/repositories/{id}/edge-history?from=A&to=B");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bob);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Put_invalid_rules_returns_bad_request_and_stores_nothing()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var created = await AdminPostAsync(Payload($"rules-{suffix}", $"/repos/local/rules-{suffix}"));
        var id = JsonDocument.Parse(await created.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();

        var invalid = new { yaml = "rules:\n  - name: \"No constraint\"\n    severity: error\n" };
        var response = await PutAsync(AdminKey, $"/api/repositories/{id}/rules", invalid);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var db = CreateDb();
        var repo = await db.Repositories.SingleAsync(r => r.Id == id);
        Assert.Null(repo.RulesYaml);
    }

    [Fact]
    public async Task Put_valid_rules_stores_yaml_and_returns_parsed()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var created = await AdminPostAsync(Payload($"rules-{suffix}", $"/repos/local/rules-{suffix}"));
        var id = JsonDocument.Parse(await created.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();

        var yaml = "rules:\n  - name: \"Domain must not depend on Infrastructure\"\n    severity: error\n    deny:\n      from: { path: \"Tessera.Domain\" }\n      to: { path: \"Tessera.Infrastructure\" }\n";
        var response = await PutAsync(AdminKey, $"/api/repositories/{id}/rules", new { yaml });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var db = CreateDb();
        var repo = await db.Repositories.SingleAsync(r => r.Id == id);
        Assert.Equal(yaml, repo.RulesYaml);
    }

    [Fact]
    public async Task Rules_endpoints_require_access_to_repository()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var alice = await SeedUserAsync($"alice-{suffix}");
        var bob = await SeedUserAsync($"bob-{suffix}");

        var created = await PostAsync(alice, Payload($"rules-{suffix}", $"/repos/local/rules-{suffix}"));
        var id = JsonDocument.Parse(await created.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/repositories/{id}/rules");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bob);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Available_without_token_is_rejected()
    {
        var response = await _client.GetAsync("/api/repositories/local/available");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Available_lists_git_folders_and_marks_registered()
    {
        var root = Path.Combine(Path.GetTempPath(), "tessera-local-available-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "alpha", ".git"));
            Directory.CreateDirectory(Path.Combine(root, "beta"));
            Directory.CreateDirectory(Path.Combine(root, "gamma"));
            File.WriteAllText(Path.Combine(root, "gamma", ".git"), "gitdir: /elsewhere");
            Directory.CreateDirectory(Path.Combine(root, "nested", "deep", "repo", ".git"));

            var alphaPath = Path.Combine(root, "alpha").Replace('\\', '/');
            await using (var db = CreateDb())
            {
                db.Repositories.Add(new Repository
                {
                    Id = Guid.NewGuid(),
                    GitHubId = 0,
                    Owner = "local",
                    Name = "alpha",
                    FullName = "alpha",
                    DefaultBranch = "main",
                    CloneUrl = alphaPath,
                    InstallationId = 0,
                    CreatedBy = "admin",
                    IsConnected = false,
                    Status = ProcessingStatus.Pending,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                });
                await db.SaveChangesAsync();
            }

            var client = _factory.WithWebHostBuilder(b => b.UseSetting("LocalRepos:Root", root)).CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/repositories/local/available");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminKey);
            var response = await client.SendAsync(request);

            response.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(root, doc.RootElement.GetProperty("root").GetString());
            var repos = doc.RootElement.GetProperty("repos");
            Assert.Equal(3, repos.GetArrayLength());

            Assert.Equal("alpha", repos[0].GetProperty("name").GetString());
            Assert.Equal(alphaPath, repos[0].GetProperty("path").GetString());
            Assert.True(repos[0].GetProperty("registered").GetBoolean());

            Assert.Equal("gamma", repos[1].GetProperty("name").GetString());
            Assert.False(repos[1].GetProperty("registered").GetBoolean());

            Assert.Equal("repo", repos[2].GetProperty("name").GetString());
            Assert.Equal(
                Path.Combine(root, "nested", "deep", "repo").Replace('\\', '/'),
                repos[2].GetProperty("path").GetString());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Delete_when_analysis_pending_then_conflict()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var created = await AdminPostAsync(Payload($"busy-{suffix}", $"/repos/local/busy-{suffix}"));
        var id = JsonDocument.Parse(await created.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();

        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/repositories/{id}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminKey);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await using var db = CreateDb();
        Assert.True(await db.Repositories.AnyAsync(r => r.Id == id));
    }

    [Fact]
    public async Task Delete_removes_repository_and_all_related_rows()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var created = await AdminPostAsync(Payload($"del-{suffix}", $"/repos/local/del-{suffix}"));
        var id = JsonDocument.Parse(await created.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();
        var snapshotId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();

        await using (var db = CreateDb())
        {
            var repo = await db.Repositories.SingleAsync(r => r.Id == id);
            repo.Status = ProcessingStatus.Completed;
            db.Snapshots.Add(new Snapshot { Id = snapshotId, RepositoryId = id, CommitSha = "abc123", RootHash = "root" });
            db.KnowledgeNodes.Add(new KnowledgeNode
            {
                Id = nodeId,
                RepositoryId = id,
                SnapshotId = snapshotId,
                Key = "k",
                CommitSha = "abc123",
                AnalyzedAt = DateTimeOffset.UtcNow
            });
            db.GraphEdges.Add(new GraphEdge
            {
                Id = Guid.NewGuid(),
                RepositoryId = id,
                SnapshotId = snapshotId,
                FromNodeId = nodeId,
                FromKey = "a",
                ToNodeId = nodeId,
                ToKey = "b"
            });
            db.KnowledgeNodeProvenances.Add(new KnowledgeNodeProvenance
            {
                Id = Guid.NewGuid(),
                NodeId = nodeId,
                CommitSha = "abc123",
                GeneratedAt = DateTimeOffset.UtcNow
            });
            db.NodeEmbeddings.Add(new NodeEmbedding
            {
                Id = Guid.NewGuid(),
                NodeId = nodeId,
                SnapshotId = snapshotId,
                RepositoryId = id,
                CreatedAt = DateTimeOffset.UtcNow
            });
            db.ConversationMessages.Add(new ConversationMessage { Id = Guid.NewGuid(), RepositoryId = id });
            await db.SaveChangesAsync();
        }

        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/repositories/{id}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminKey);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await using (var db = CreateDb())
        {
            Assert.False(await db.Repositories.AnyAsync(r => r.Id == id));
            Assert.False(await db.Snapshots.AnyAsync(s => s.RepositoryId == id));
            Assert.False(await db.KnowledgeNodes.AnyAsync(n => n.RepositoryId == id));
            Assert.False(await db.GraphEdges.AnyAsync(e => e.RepositoryId == id));
            Assert.False(await db.KnowledgeNodeProvenances.AnyAsync(p => p.NodeId == nodeId));
            Assert.False(await db.NodeEmbeddings.AnyAsync(e => e.RepositoryId == id));
            Assert.False(await db.ConversationMessages.AnyAsync(m => m.RepositoryId == id));
        }
    }

    private Task<HttpResponseMessage> AdminPostAsync(object body)
        => PostAsync(AdminKey, body);

    private Task<HttpResponseMessage> AdminPostAsync(string path, object body)
        => PostAsync(AdminKey, path, body);

    private async Task<HttpResponseMessage> PutAsync(string token, string path, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, path)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _client.SendAsync(request);
    }

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
