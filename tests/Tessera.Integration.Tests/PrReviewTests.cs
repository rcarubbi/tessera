using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Tessera.Domain.Entities;
using Tessera.Domain.Enums;
using Tessera.Domain.Ports;
using Tessera.Infrastructure.Ai;
using Tessera.Infrastructure.Analysis;
using Tessera.Infrastructure.Data;
using Tessera.Infrastructure.GitHub;
using Tessera.Infrastructure.Queries;
using Tessera.Infrastructure.Reviews;

namespace Tessera.Integration.Tests;

public sealed class PrReviewServiceTests
{
    private static readonly Guid Repo = Guid.NewGuid();

    [Fact]
    public async Task ProcessAsync_posts_comment_and_marks_posted()
    {
        using var db = CreateDb();
        var repo = SeedRepo(db, EnablePrComments: true);
        SeedSnapshot(db, "base1",
            Node("Impl", "src/app/Impl.cs", NodeKind.Class, 10),
            Node("Svc", "src/app/Svc.cs", NodeKind.Class, 20));
        SeedSnapshot(db, "head1",
            Node("Impl", "src/app/Impl.cs", NodeKind.Class, 10),
            Node("Svc", "src/app/Svc.cs", NodeKind.Class, 20),
            Edge("Impl", "Svc", EdgeType.Calls));
        var review = SeedReview(db, repo.Id, 12, "head1", "base1");

        var github = new RecordingGitHubClient();
        var service = CreateService(db, github);
        await service.ProcessAsync(repo, review, "work");

        Assert.Equal(PrReviewStatus.Posted, review.Status);
        Assert.Equal(100, review.CommentId);
        Assert.Null(review.ErrorMessage);
        Assert.Contains("## Tessera PR analysis", review.CommentBody);
        Assert.Contains("src/app/Impl.cs", review.CommentBody);
        Assert.Contains("Svc", review.CommentBody);
        Assert.Contains("### New dependencies", review.CommentBody);
        Assert.Contains("`Impl` → `Svc` — Calls", review.CommentBody);
        Assert.Single(github.Posts);
    }

    [Fact]
    public async Task ProcessAsync_does_not_repost_when_already_posted()
    {
        using var db = CreateDb();
        var repo = SeedRepo(db, EnablePrComments: true);
        SeedSnapshot(db, "base1", Node("A", "src/app/A.cs", NodeKind.Class, 1));
        SeedSnapshot(db, "head1", Node("A", "src/app/A.cs", NodeKind.Class, 1));
        var review = SeedReview(db, repo.Id, 12, "head1", "base1");

        var github = new RecordingGitHubClient();
        var service = CreateService(db, github);
        await service.ProcessAsync(repo, review, "work");
        Assert.Equal(PrReviewStatus.Posted, review.Status);

        await service.ProcessAsync(repo, review, "work");

        Assert.Single(github.Posts);
        Assert.Equal(PrReviewStatus.Posted, review.Status);
    }

    [Fact]
    public async Task ProcessAsync_deletes_previous_comment_on_head_change()
    {
        using var db = CreateDb();
        var repo = SeedRepo(db, EnablePrComments: true);
        SeedSnapshot(db, "base1", Node("A", "src/app/A.cs", NodeKind.Class, 1));
        SeedSnapshot(db, "head2", Node("A", "src/app/A.cs", NodeKind.Class, 1));
        SeedReview(db, repo.Id, 12, "head1", "base1", PrReviewStatus.Posted, CommentId: 99);
        var review = SeedReview(db, repo.Id, 12, "head2", "base1");

        var github = new RecordingGitHubClient();
        var service = CreateService(db, github);
        await service.ProcessAsync(repo, review, "work");

        Assert.Contains(99, github.Deleted);
        Assert.Single(github.Posts);
        Assert.Equal(PrReviewStatus.Posted, review.Status);
        Assert.Equal(100, review.CommentId);
    }

    [Fact]
    public async Task ProcessAsync_skips_post_when_comments_disabled()
    {
        using var db = CreateDb();
        var repo = SeedRepo(db, EnablePrComments: false);
        SeedSnapshot(db, "base1", Node("A", "src/app/A.cs", NodeKind.Class, 1));
        SeedSnapshot(db, "head1", Node("A", "src/app/A.cs", NodeKind.Class, 1));
        var review = SeedReview(db, repo.Id, 12, "head1", "base1");

        var github = new RecordingGitHubClient();
        var service = CreateService(db, github);
        await service.ProcessAsync(repo, review, "work");

        Assert.Equal(PrReviewStatus.Reviewed, review.Status);
        Assert.Empty(github.Posts);
        Assert.Null(review.CommentId);
        Assert.NotNull(review.CommentBody);
    }

    [Fact]
    public async Task ProcessAsync_stays_queued_without_head_snapshot()
    {
        using var db = CreateDb();
        var repo = SeedRepo(db, EnablePrComments: true);
        var review = SeedReview(db, repo.Id, 12, "missing-head", "base1");

        var github = new RecordingGitHubClient();
        var service = CreateService(db, github);
        await service.ProcessAsync(repo, review, "work");

        Assert.Equal(PrReviewStatus.Queued, review.Status);
        Assert.Null(review.CommentBody);
        Assert.Empty(github.Posts);
    }

    [Fact]
    public async Task ProcessAsync_marks_failed_on_post_error_and_retries()
    {
        using var db = CreateDb();
        var repo = SeedRepo(db, EnablePrComments: true);
        SeedSnapshot(db, "base1", Node("A", "src/app/A.cs", NodeKind.Class, 1));
        SeedSnapshot(db, "head1", Node("A", "src/app/A.cs", NodeKind.Class, 1));
        var review = SeedReview(db, repo.Id, 12, "head1", "base1");

        var github = new RecordingGitHubClient(failFirstPost: true);
        var service = CreateService(db, github);

        await service.ProcessAsync(repo, review, "work");
        Assert.Equal(PrReviewStatus.Failed, review.Status);
        Assert.NotNull(review.ErrorMessage);

        await service.ProcessAsync(repo, review, "work");
        Assert.Equal(PrReviewStatus.Posted, review.Status);
        Assert.Null(review.ErrorMessage);
        Assert.Equal(100, review.CommentId);
    }

    [Fact]
    public async Task ProcessAsync_includes_ai_summary_when_provider_available()
    {
        using var db = CreateDb();
        var repo = SeedRepo(db, EnablePrComments: true);
        SeedSnapshot(db, "base1", Node("A", "src/app/A.cs", NodeKind.Class, 1));
        SeedSnapshot(db, "head1", Node("A", "src/app/A.cs", NodeKind.Class, 1));
        var review = SeedReview(db, repo.Id, 12, "head1", "base1");

        var provider = new FakeChatProvider("fake", "model", _ => "Adds a new dependency on the service layer.");
        var github = new RecordingGitHubClient();
        var service = CreateService(db, github, new FakeProviderRegistry(provider));

        await service.ProcessAsync(repo, review, "work");

        Assert.Equal(PrReviewStatus.Posted, review.Status);
        Assert.Contains("### Summary", review.CommentBody);
        Assert.Contains("Adds a new dependency on the service layer.", review.CommentBody);
        Assert.Equal(1, provider.Calls);
    }

    [Fact]
    public async Task ListAsync_orders_by_updated_at_desc()
    {
        using var db = CreateDb();
        var repo = SeedRepo(db, EnablePrComments: true);
        var older = SeedReview(db, repo.Id, 11, "head-old", "base1", createdAt: DateTimeOffset.UtcNow.AddMinutes(-10));
        var newer = SeedReview(db, repo.Id, 12, "head-new", "base1", createdAt: DateTimeOffset.UtcNow);

        var service = CreateService(db, new RecordingGitHubClient());
        var result = await service.ListAsync(repo.Id);

        Assert.Equal(2, result.Items.Count);
        Assert.Equal(newer.Id, result.Items[0].Id);
        Assert.Equal(older.Id, result.Items[1].Id);
        Assert.Equal("Queued", result.Items[0].Status);
    }

    [Fact]
    public void Render_omits_rules_section_when_not_evaluated()
    {
        var report = new PrReport(
            "head1", "base1",
            ["src/app/A.cs"],
            Array.Empty<PrImpactItem>(),
            Array.Empty<PrNewEdge>(),
            EdgeDeltaUnavailable: false,
            Array.Empty<RuleViolation>(),
            RulesEvaluated: false,
            AiSummary: null,
            ["No architecture rules configured; rules section omitted."]);

        var body = PrReviewService.Render(report);

        Assert.Contains("Not evaluated — no rules configured.", body);
        Assert.Contains("No new dependencies between base and head.", body);
    }

    private PrReviewService CreateService(
        TesseraDbContext db,
        RecordingGitHubClient github,
        IProviderRegistry? providers = null) =>
        new(
            db,
            new GraphQueryService(db),
            new ArchitectureRuleService(db),
            github,
            providers ?? new FakeProviderRegistry(null),
            new FakeGitClient(["src/app/Impl.cs", "src/app/A.cs"]),
            NullLogger<PrReviewService>.Instance);

    private static Repository SeedRepo(TesseraDbContext db, bool EnablePrComments)
    {
        var repo = new Repository
        {
            Id = Repo,
            GitHubId = 555,
            Owner = "acme",
            Name = "sample",
            FullName = "acme/sample",
            InstallationId = 7,
            IsConnected = true,
            EnablePrComments = EnablePrComments,
            Status = ProcessingStatus.Completed,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.Repositories.Add(repo);
        db.SaveChanges();
        return repo;
    }

    private static PullRequestReview SeedReview(
        TesseraDbContext db,
        Guid repositoryId,
        int prNumber,
        string headSha,
        string baseSha,
        PrReviewStatus status = PrReviewStatus.Queued,
        long? CommentId = null,
        DateTimeOffset? createdAt = null)
    {
        var review = new PullRequestReview
        {
            Id = Guid.NewGuid(),
            RepositoryId = repositoryId,
            PrNumber = prNumber,
            HeadSha = headSha,
            BaseSha = baseSha,
            Status = status,
            CommentId = CommentId,
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow,
            UpdatedAt = createdAt ?? DateTimeOffset.UtcNow
        };
        db.PullRequestReviews.Add(review);
        db.SaveChanges();
        return review;
    }

    private static KnowledgeNode Node(string key, string path, NodeKind kind, int line) => new()
    {
        Id = Guid.NewGuid(),
        RepositoryId = Repo,
        Key = key,
        Path = path,
        Symbol = key,
        Kind = kind,
        StartLine = line,
        EndLine = line,
        Content = "",
        Confidence = 1.0,
        CommitSha = ""
    };

    private static GraphEdge Edge(string from, string to, EdgeType type) => new()
    {
        Id = Guid.NewGuid(),
        RepositoryId = Repo,
        FromKey = from,
        ToKey = to,
        Type = type,
        Confidence = 1.0,
        IsStatic = true
    };

    private static Snapshot SeedSnapshot(TesseraDbContext db, string sha, params object[] nodesAndEdges)
    {
        var snapshot = new Snapshot
        {
            Id = Guid.NewGuid(),
            RepositoryId = Repo,
            CommitSha = sha,
            RootHash = $"root-{sha}",
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.Snapshots.Add(snapshot);
        foreach (var item in nodesAndEdges)
        {
            switch (item)
            {
                case KnowledgeNode node:
                    node.SnapshotId = snapshot.Id;
                    db.KnowledgeNodes.Add(node);
                    break;
                case GraphEdge edge:
                    edge.SnapshotId = snapshot.Id;
                    db.GraphEdges.Add(edge);
                    break;
            }
        }
        db.SaveChanges();
        return snapshot;
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

public sealed class PullRequestWebhookTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly string _secret;
    private readonly Guid _dbName;

    public PullRequestWebhookTests(WebApplicationFactory<Program> factory)
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
    public async Task Pull_request_opened_queues_review_and_sets_repo_pending()
    {
        await SeedRepoAsync(555, "acme/sample");
        var body = PullRequestPayload("opened", 555, "abc123", "def456");

        var response = await PostWebhookAsync("pull_request", body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var db = CreateDb();
        var repo = await db.Repositories.SingleAsync(r => r.GitHubId == 555);
        Assert.Equal(ProcessingStatus.Pending, repo.Status);

        var review = await db.PullRequestReviews.SingleAsync(r => r.RepositoryId == repo.Id);
        Assert.Equal(12, review.PrNumber);
        Assert.Equal("abc123", review.HeadSha);
        Assert.Equal("def456", review.BaseSha);
        Assert.Equal(PrReviewStatus.Queued, review.Status);
    }

    [Fact]
    public async Task Pull_request_duplicate_head_keeps_single_review()
    {
        await SeedRepoAsync(555, "acme/sample");
        var body = PullRequestPayload("synchronize", 555, "abc123", "def456");

        await PostWebhookAsync("pull_request", body);
        await PostWebhookAsync("pull_request", body);

        using var db = CreateDb();
        Assert.Equal(1, await db.PullRequestReviews.CountAsync());
    }

    [Fact]
    public async Task Pull_request_for_unknown_repo_is_ignored()
    {
        var body = PullRequestPayload("opened", 999, "abc123", "def456");

        var response = await PostWebhookAsync("pull_request", body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var db = CreateDb();
        Assert.Equal(0, await db.PullRequestReviews.CountAsync());
    }

    [Fact]
    public async Task Pull_request_synchronize_requeues_failed_review()
    {
        var (repoId, reviewId) = await SeedReviewAsync(555, "acme/sample", 12, "abc123", "def456", PrReviewStatus.Failed);
        var body = PullRequestPayload("synchronize", 555, "abc123", "def456");

        var response = await PostWebhookAsync("pull_request", body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var db = CreateDb();
        var review = await db.PullRequestReviews.SingleAsync(r => r.Id == reviewId);
        Assert.Equal(PrReviewStatus.Queued, review.Status);
        Assert.Equal(1, await db.PullRequestReviews.CountAsync());
    }

    [Fact]
    public async Task Pull_request_closed_is_ignored()
    {
        await SeedRepoAsync(555, "acme/sample");
        var body = PullRequestPayload("closed", 555, "abc123", "def456");

        var response = await PostWebhookAsync("pull_request", body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var db = CreateDb();
        Assert.Equal(0, await db.PullRequestReviews.CountAsync());
    }

    private async Task<HttpResponseMessage> PostWebhookAsync(string eventName, string payload)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/github/webhook");
        request.Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");
        request.Headers.Add("X-GitHub-Event", eventName);
        request.Headers.Add("X-Hub-Signature-256", Sign(payload));
        return await _client.SendAsync(request);
    }

    private string Sign(string payload)
    {
        using var hmac = new System.Security.Cryptography.HMACSHA256(System.Text.Encoding.UTF8.GetBytes(_secret));
        return "sha256=" + Convert.ToHexStringLower(hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(payload)));
    }

    private static string PullRequestPayload(string action, long repoId, string headSha, string baseSha) =>
        $$"""
        {
          "action": "{{action}}",
          "number": 12,
          "pull_request": {
            "number": 12,
            "head": { "sha": "{{headSha}}" },
            "base": { "sha": "{{baseSha}}" }
          },
          "repository": {
            "id": {{repoId}},
            "name": "sample",
            "full_name": "acme/sample"
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

    private async Task<(Guid RepoId, Guid ReviewId)> SeedReviewAsync(long gitHubId, string fullName, int prNumber, string headSha, string baseSha, PrReviewStatus status)
    {
        await using var db = CreateDb();
        var repo = new Repository
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
        };
        db.Repositories.Add(repo);
        var review = new PullRequestReview
        {
            Id = Guid.NewGuid(),
            RepositoryId = repo.Id,
            PrNumber = prNumber,
            HeadSha = headSha,
            BaseSha = baseSha,
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.PullRequestReviews.Add(review);
        await db.SaveChangesAsync();
        return (repo.Id, review.Id);
    }

    private TesseraDbContext CreateDb() =>
        _factory.Services.CreateScope().ServiceProvider.GetRequiredService<TesseraDbContext>();

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }
}

public sealed class PrReviewEndpointTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private const string AdminKey = "test-admin-key";

    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly Guid _dbName;

    public PrReviewEndpointTests(WebApplicationFactory<Program> factory)
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
        var response = await _client.GetAsync("/api/repositories/00000000-0000-0000-0000-000000000000/pr-reviews");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Admin_lists_pr_reviews_for_repository()
    {
        var id = await SeedRepoWithReviewAsync();

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/repositories/{id}/pr-reviews");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminKey);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        var items = body.GetProperty("items");
        Assert.Equal(1, items.GetArrayLength());
        Assert.Equal(12, items[0].GetProperty("prNumber").GetInt32());
        Assert.Equal("Posted", items[0].GetProperty("status").GetString());
    }

    [Fact]
    public async Task Non_admin_cannot_list_others_pr_reviews()
    {
        var id = await SeedRepoWithReviewAsync(createdBy: "alice");

        var token = await SeedUserAsync("bob");
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/repositories/{id}/pr-reviews");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Admin_can_toggle_pr_comments_via_settings()
    {
        var id = await SeedRepoWithReviewAsync();

        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/repositories/{id}/settings")
        {
            Content = JsonContent.Create(new { enablePrComments = false })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminKey);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.False(body.GetProperty("enablePrComments").GetBoolean());

        using var db = CreateDb();
        Assert.False((await db.Repositories.SingleAsync(r => r.Id == id)).EnablePrComments);
    }

    private async Task<Guid> SeedRepoWithReviewAsync(string? createdBy = null)
    {
        await using var db = CreateDb();
        var repo = new Repository
        {
            GitHubId = 555,
            Owner = "acme",
            Name = "sample",
            FullName = "acme/sample",
            CloneUrl = "https://github.com/acme/sample.git",
            DefaultBranch = "main",
            InstallationId = 7,
            CreatedBy = createdBy ?? "alice",
            IsConnected = true,
            Status = ProcessingStatus.Completed,
            EnablePrComments = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.Repositories.Add(repo);
        db.PullRequestReviews.Add(new PullRequestReview
        {
            Id = Guid.NewGuid(),
            RepositoryId = repo.Id,
            PrNumber = 12,
            HeadSha = "abc123",
            BaseSha = "def456",
            Status = PrReviewStatus.Posted,
            CommentId = 100,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        return repo.Id;
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

    private TesseraDbContext CreateDb() =>
        _factory.Services.CreateScope().ServiceProvider.GetRequiredService<TesseraDbContext>();

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }
}
