using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Tessera.Domain.Entities;
using Tessera.Domain.Enums;
using Tessera.Infrastructure.Data;
using Tessera.Infrastructure.Queries;

namespace Tessera.Integration.Tests;

public sealed class EvidenceTransparencyTests
{
    private static readonly Guid Repo = Guid.NewGuid();

    [Fact]
    public void Static_edge_classified_as_fact_with_ast_source()
    {
        var result = EvidenceClassifier.ClassifyEdge(true, 1.0);

        Assert.Equal("fact", result.Classification);
        Assert.Equal("AST", result.FactSource);
    }

    [Fact]
    public void Non_static_or_low_confidence_edge_classified_as_inference()
    {
        Assert.Equal("inference", EvidenceClassifier.ClassifyEdge(false, 1.0).Classification);
        Assert.Equal("inference", EvidenceClassifier.ClassifyEdge(true, 0.8).Classification);
        Assert.Equal("Inference", EvidenceClassifier.ClassifyEdge(true, 0.8).FactSource);
    }

    [Fact]
    public void Static_node_classified_as_fact_with_ast_source()
    {
        var result = EvidenceClassifier.ClassifyNode(null, 1.0, ReviewStatus.None);

        Assert.Equal("fact", result.Classification);
        Assert.Equal("AST", result.FactSource);
    }

    [Fact]
    public void Node_with_model_or_below_full_confidence_classified_as_inference()
    {
        var withModel = EvidenceClassifier.ClassifyNode("gpt-4o", 1.0, ReviewStatus.None);
        Assert.Equal("inference", withModel.Classification);
        Assert.Equal("Inference", withModel.FactSource);

        var lowConfidence = EvidenceClassifier.ClassifyNode(null, 0.85, ReviewStatus.None);
        Assert.Equal("inference", lowConfidence.Classification);
    }

    [Theory]
    [InlineData(0.95, "verified")]
    [InlineData(0.9, "verified")]
    [InlineData(0.89, "accepted")]
    [InlineData(0.7, "accepted")]
    [InlineData(0.69, "low-confidence")]
    [InlineData(0.0, "low-confidence")]
    public void Tier_follows_documented_boundaries(double confidence, string expected)
    {
        var result = EvidenceClassifier.ClassifyNode(null, confidence, ReviewStatus.None);

        Assert.Equal(expected, result.Tier);
    }

    [Fact]
    public void Accepted_review_status_promotes_low_confidence_node_to_verified()
    {
        var result = EvidenceClassifier.ClassifyNode(null, 0.6, ReviewStatus.Accepted);

        Assert.Equal("verified", result.Tier);
    }

    [Fact]
    public async Task Graph_returns_classification_and_provenance_fields()
    {
        using var db = CreateDb();
        var snap = Snapshot("s1", 0);
        await SeedAsync(db, snap, new[]
        {
            FactEdge("B", "A"),
            AiEdge("C", "A", snap)
        });
        var service = new GraphQueryService(db);

        var graph = await service.GraphAsync(Repo);

        var node = graph.Nodes.Single(n => n.Key == "C");
        Assert.Equal("inference", node.Classification);
        Assert.Equal("Inference", node.FactSource);
        Assert.Equal("low-confidence", node.Tier);
        Assert.Equal("c1", node.CommitSha);
        Assert.Equal("gpt-4o", node.Model);
        Assert.Equal("prompt-v1", node.PromptVersion);
        var edge = graph.Edges.Single(e => e.From == "B" && e.To == "A");
        Assert.Equal("fact", edge.Classification);
        Assert.Equal("AST", edge.FactSource);
        Assert.Equal("b.cs:3", edge.Evidence);
    }

    [Fact]
    public async Task Graph_source_facts_filter_returns_only_fact_items()
    {
        using var db = CreateDb();
        var snap = Snapshot("s1", 0);
        await SeedAsync(db, snap, new[]
        {
            FactEdge("B", "A"),
            AiEdge("C", "A", snap)
        });
        var service = new GraphQueryService(db);

        var graph = await service.GraphAsync(Repo, source: "facts");

        Assert.All(graph.Nodes, n => Assert.Equal("fact", n.Classification));
        Assert.All(graph.Edges, e => Assert.Equal("fact", e.Classification));
        Assert.Contains(graph.Nodes, n => n.Key == "B");
        Assert.DoesNotContain(graph.Nodes, n => n.Key == "C");
    }

    [Fact]
    public async Task Graph_tier_filter_returns_only_matching_tier()
    {
        using var db = CreateDb();
        var snap = Snapshot("s1", 0);
        await SeedAsync(db, snap, new[]
        {
            FactEdge("B", "A"),
            AiEdge("C", "A", snap)
        });
        var service = new GraphQueryService(db);

        var graph = await service.GraphAsync(Repo, tier: "low-confidence");

        Assert.All(graph.Nodes, n => Assert.Equal("low-confidence", n.Tier));
        Assert.All(graph.Edges, e => Assert.Equal("low-confidence", e.Tier));
        Assert.Contains(graph.Nodes, n => n.Key == "C");
    }

    private static GraphEdge FactEdge(string from, string to) => new()
    {
        Id = Guid.NewGuid(), RepositoryId = Repo, FromKey = from, ToKey = to,
        Type = EdgeType.Calls, Evidence = $"{from.ToLowerInvariant()}.cs:3",
        Confidence = 1.0, IsStatic = true
    };

    private static GraphEdge AiEdge(string from, string to, Snapshot snap) => new()
    {
        Id = Guid.NewGuid(), RepositoryId = Repo, FromKey = from, ToKey = to,
        Type = EdgeType.FieldDependency, Evidence = $"{from.ToLowerInvariant()}.cs:9",
        Confidence = 0.6, IsStatic = false, SnapshotId = snap.Id
    };

    private static KnowledgeNode Node(Guid snapshotId, string key, string? model = null, double confidence = 1.0, ReviewStatus reviewStatus = ReviewStatus.None) => new()
    {
        Id = Guid.NewGuid(), RepositoryId = Repo, SnapshotId = snapshotId, Key = key,
        Path = $"{key}.cs", Symbol = key, Kind = NodeKind.Class, Language = "csharp",
        StructuralHash = $"h-{key}", SemanticHash = $"s-{key}", Content = $"# {key}",
        StartLine = 1, CommitSha = $"{key.ToLowerInvariant()}1",
        Model = model, PromptVersion = model is null ? null : "prompt-v1",
        AnalyzedAt = DateTimeOffset.UtcNow, Confidence = confidence, ReviewStatus = reviewStatus
    };

    private static Snapshot Snapshot(string sha, int seq) => new()
    {
        Id = Guid.NewGuid(), RepositoryId = Repo, CommitSha = sha,
        RootHash = $"root-{sha}", NodeCount = seq, EdgeCount = seq, CreatedAt = DateTimeOffset.UtcNow
    };

    private static async Task SeedAsync(TesseraDbContext db, Snapshot snapshot, IEnumerable<GraphEdge> edges)
    {
        db.Snapshots.Add(snapshot);
        foreach (var edge in edges)
        {
            edge.SnapshotId = snapshot.Id;
            db.GraphEdges.Add(edge);
        }
        foreach (var key in edges.SelectMany(e => new[] { e.FromKey, e.ToKey }).Distinct())
        {
            var model = key == "C" ? "gpt-4o" : null;
            var confidence = key == "C" ? 0.6 : 1.0;
            db.KnowledgeNodes.Add(Node(snapshot.Id, key, model, confidence));
        }
        await db.SaveChangesAsync();
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

public sealed class EvidenceTransparencyEndpointTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private const string AdminKey = "test-admin-key";

    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly Guid _dbName;

    public EvidenceTransparencyEndpointTests(WebApplicationFactory<Program> factory)
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
    public async Task Graph_returns_403_without_access()
    {
        var id = await SeedRepoAsync();
        var token = await SeedUserAsync("bob");
        await using (var db = CreateDb())
        {
            var user = await db.GitHubUsers.SingleAsync(u => u.Login == "bob");
            user.InstallationIdsJson = "[]";
            await db.SaveChangesAsync();
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/repositories/{id}/graph");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Graph_response_includes_classification_fields()
    {
        var id = await SeedRepoAsync();
        var token = await SeedUserAsync("bob");
        await using (var db = CreateDb())
        {
            var user = await db.GitHubUsers.SingleAsync(u => u.Login == "bob");
            user.InstallationIdsJson = "[7]";
            await db.SaveChangesAsync();
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/repositories/{id}/graph");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        var firstNode = body.GetProperty("nodes").EnumerateArray().First();
        Assert.True(firstNode.TryGetProperty("classification", out _));
        Assert.True(firstNode.TryGetProperty("factSource", out _));
        Assert.True(firstNode.TryGetProperty("tier", out _));
    }

    private async Task<Guid> SeedRepoAsync()
    {
        await using var db = CreateDb();
        var repo = new Repository
        {
            Id = Guid.NewGuid(),
            GitHubId = 601,
            Owner = "acme",
            Name = "evidence",
            FullName = "acme/evidence",
            CloneUrl = "https://github.com/acme/evidence.git",
            DefaultBranch = "main",
            InstallationId = 7,
            CreatedBy = "alice",
            IsConnected = true,
            Status = ProcessingStatus.Completed,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.Repositories.Add(repo);
        var snapshotId = Guid.NewGuid();
        db.Snapshots.Add(new Snapshot
        {
            Id = snapshotId, RepositoryId = repo.Id, CommitSha = "abc",
            RootHash = "root", NodeCount = 1, EdgeCount = 0, CreatedAt = DateTimeOffset.UtcNow
        });
        db.KnowledgeNodes.Add(new KnowledgeNode
        {
            Id = Guid.NewGuid(), RepositoryId = repo.Id, SnapshotId = snapshotId,
            Key = "Order", Path = "Order.cs", Symbol = "Order", Kind = NodeKind.Class,
            Language = "csharp", StructuralHash = "h", SemanticHash = "s", Content = "# Order",
            StartLine = 1, CommitSha = "abc", Confidence = 1.0, AnalyzedAt = DateTimeOffset.UtcNow
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
