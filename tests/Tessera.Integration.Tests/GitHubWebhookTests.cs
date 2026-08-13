using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tessera.Domain.Entities;
using Tessera.Domain.Enums;
using Tessera.Infrastructure.Data;
using Tessera.Infrastructure.GitHub;

namespace Tessera.Integration.Tests;

public sealed class GitHubWebhookSignatureTests
{
    private const string Secret = "super-secret";

    [Fact]
    public void Verify_accepts_valid_signature()
    {
        var body = Encoding.UTF8.GetBytes("{}");
        var signature = Sign(Secret, body);

        Assert.True(GitHubWebhookSignature.Verify(Secret, body, signature));
    }

    [Fact]
    public void Verify_rejects_tampered_body()
    {
        var body = Encoding.UTF8.GetBytes("{\"a\":1}");
        var signature = Sign(Secret, body);

        Assert.False(GitHubWebhookSignature.Verify(Secret, Encoding.UTF8.GetBytes("{\"a\":2}"), signature));
    }

    [Fact]
    public void Verify_rejects_wrong_secret()
    {
        var body = Encoding.UTF8.GetBytes("{}");

        Assert.False(GitHubWebhookSignature.Verify("other-secret", body, Sign(Secret, body)));
    }

    [Fact]
    public void Verify_rejects_missing_or_malformed_header()
    {
        var body = Encoding.UTF8.GetBytes("{}");

        Assert.False(GitHubWebhookSignature.Verify(Secret, body, null));
        Assert.False(GitHubWebhookSignature.Verify(Secret, body, "sha1=abc"));
        Assert.False(GitHubWebhookSignature.Verify(Secret, body, "sha256=tooshort"));
    }

    private static string Sign(string secret, byte[] body)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return "sha256=" + Convert.ToHexStringLower(hmac.ComputeHash(body));
    }
}

public sealed class GitHubWebhookEndpointTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly string _secret;
    private readonly Guid _dbName;

    public GitHubWebhookEndpointTests(WebApplicationFactory<Program> factory)
    {
        _secret = "test-secret";
        _dbName = Guid.NewGuid();

        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("MigrateOnStartup", "false");
            builder.UseSetting("Database:InMemory", "true");
            builder.UseSetting("Database:Name", _dbName.ToString());
            builder.UseSetting("GitHub:AppId", "123");
            builder.UseSetting("GitHub:PrivateKeyPath", "x.pem");
            builder.UseSetting("GitHub:WebhookSecret", _secret);
        });

        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task Push_for_unknown_repo_is_ignored()
    {
        using var db = CreateDb();
        var body = PushPayload("unknown/repo", 999);
        var response = await PostWebhookAsync("push", body);

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, await db.Repositories.CountAsync());
    }

    [Fact]
    public async Task Push_for_known_repo_sets_status_pending()
    {
        await SeedRepoAsync(555, "acme/sample");
        var body = PushPayload("acme/sample", 555);

        var response = await PostWebhookAsync("push", body);
        var serverBody = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == System.Net.HttpStatusCode.OK, $"expected OK, got {response.StatusCode}: {serverBody}");

        using var db = CreateDb();
        var repo = await db.Repositories.SingleAsync(r => r.GitHubId == 555);
        Assert.Equal(ProcessingStatus.Pending, repo.Status);
        Assert.Equal("https://github.com/acme/sample.git", repo.CloneUrl);
    }

    [Fact]
    public async Task Invalid_signature_is_rejected()
    {
        await SeedRepoAsync(555, "acme/sample");
        var body = PushPayload("acme/sample", 555);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/github/webhook");
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        request.Headers.Add("X-GitHub-Event", "push");
        request.Headers.Add("X-Hub-Signature-256", "sha256=" + new string('0', 64));

        var response = await _client.SendAsync(request);

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Malformed_json_payload_is_rejected()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/github/webhook");
        var bytes = Encoding.UTF8.GetBytes("{ not valid json");
        request.Content = new ByteArrayContent(bytes);
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        request.Headers.Add("X-GitHub-Event", "push");
        request.Headers.Add("X-Hub-Signature-256", Sign(bytes));

        var response = await _client.SendAsync(request);

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Installation_removed_disconnects_repos()
    {
        await SeedRepoAsync(555, "acme/sample");
        var body = """{"action":"removed","installation":{"id":7,"account":{}}}""";

        var response = await PostWebhookAsync("installation", body);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

        using var db = CreateDb();
        var repo = await db.Repositories.SingleAsync(r => r.GitHubId == 555);
        Assert.False(repo.IsConnected);
    }

    [Fact]
    public async Task Installation_created_registers_repositories()
    {
        var body = """
            {
              "action": "created",
              "installation": { "id": 7, "account": { "login": "acme" } },
              "repositories": [
                {
                  "id": 555,
                  "name": "sample",
                  "full_name": "acme/sample",
                  "owner": { "login": "acme" },
                  "clone_url": "https://github.com/acme/sample.git",
                  "default_branch": "main"
                }
              ]
            }
            """;

        var response = await PostWebhookAsync("installation", body);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

        using var db = CreateDb();
        var repo = await db.Repositories.SingleAsync(r => r.GitHubId == 555);
        Assert.Equal("acme", repo.Owner);
        Assert.Equal("acme/sample", repo.FullName);
        Assert.Equal(7, repo.InstallationId);
        Assert.Equal(ProcessingStatus.Pending, repo.Status);
        Assert.True(repo.IsConnected);
    }

    [Fact]
    public async Task Installation_created_without_clone_url_falls_back_to_github_url()
    {
        var body = """
            {
              "action": "created",
              "installation": { "id": 7, "account": { "login": "acme" } },
              "repositories": [
                {
                  "id": 556,
                  "name": "sample",
                  "full_name": "acme/sample",
                  "owner": { "login": "acme" }
                }
              ]
            }
            """;

        var response = await PostWebhookAsync("installation", body);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

        using var db = CreateDb();
        var repo = await db.Repositories.SingleAsync(r => r.GitHubId == 556);
        Assert.Equal("https://github.com/acme/sample.git", repo.CloneUrl);
    }

    [Fact]
    public async Task Setup_with_non_numeric_installation_id_returns_bad_request()
    {
        var response = await _client.GetAsync("/api/github/setup?installation_id=not-a-number&setup_action=install");

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Setup_uninstall_with_unknown_installation_returns_not_found()
    {
        var response = await _client.GetAsync("/api/github/setup?installation_id=999999&setup_action=uninstall");

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<HttpResponseMessage> PostWebhookAsync(string eventName, string payload)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/github/webhook");
        var bytes = Encoding.UTF8.GetBytes(payload);
        request.Content = new ByteArrayContent(bytes);
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        request.Headers.Add("X-GitHub-Event", eventName);
        request.Headers.Add("X-Hub-Signature-256", Sign(bytes));
        return await _client.SendAsync(request);
    }

    private string Sign(byte[] body)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_secret));
        return "sha256=" + Convert.ToHexStringLower(hmac.ComputeHash(body));
    }

    private static string PushPayload(string fullName, long id) =>
        $$"""
        {
          "ref": "refs/heads/main",
          "repository": {
            "id": {{id}},
            "name": "sample",
            "full_name": "{{fullName}}",
            "clone_url": "https://github.com/acme/sample.git",
            "default_branch": "main"
          }
        }
        """;

    private async Task SeedRepoAsync(long gitHubId, string fullName)
    {
        await using var db = CreateDb();
        db.Repositories.Add(new Repository
        {
            GitHubId = gitHubId,
            Owner = "acme",
            Name = "sample",
            FullName = fullName,
            CloneUrl = "https://github.com/acme/sample.git",
            DefaultBranch = "main",
            InstallationId = 7,
            IsConnected = true,
            Status = ProcessingStatus.Completed,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private TesseraDbContext CreateDb() =>
        _factory.Services.CreateScope().ServiceProvider.GetRequiredService<TesseraDbContext>();

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }
}
