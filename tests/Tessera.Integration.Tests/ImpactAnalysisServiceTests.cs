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

public sealed class ImpactAnalysisServiceTests
{
    private static readonly Guid Repo = Guid.NewGuid();

    [Fact]
    public async Task Metrics_derives_direct_indirect_total_and_maxDepth()
    {
        using var db = CreateDb();
        await SeedAsync(db, Snapshot("s1", 0), new[]
        {
            Edge("B", "A"), Edge("D", "A"), Edge("C", "B"), Edge("E", "D")
        });
        var service = new ImpactAnalysisService(db, new GraphQueryService(db));

        var report = await service.ReportAsync(Repo, "A");

        Assert.Equal(4, report.TotalCount);
        Assert.Equal(2, report.DirectCount);
        Assert.Equal(2, report.IndirectCount);
        Assert.Equal(2, report.MaxDepth);
    }

    [Fact]
    public async Task MaxDepth_bounds_transitive_traversal()
    {
        using var db = CreateDb();
        await SeedAsync(db, Snapshot("s1", 0), new[]
        {
            Edge("B", "A"), Edge("C", "B"), Edge("D", "C"), Edge("E", "D")
        });
        var service = new ImpactAnalysisService(db, new GraphQueryService(db));

        var report = await service.ReportAsync(Repo, "A", maxDepth: 2);

        Assert.Equal(2, report.TotalCount);
        Assert.Equal(1, report.DirectCount);
        Assert.Equal(1, report.IndirectCount);
        Assert.Equal(2, report.MaxDepth);
        Assert.Equal(new[] { "B", "C" }, report.Items.Select(i => i.Key).OrderBy(k => k).ToArray());
    }

    [Fact]
    public async Task Classifies_test_paths_and_other()
    {
        using var db = CreateDb();
        await SeedAsync(db, Snapshot("s1", 0),
            new[] { Edge("TestRunner", "A"), Edge("Helper", "A") },
            new Dictionary<string, string>
            {
                ["TestRunner"] = "tests/UnitTests/TestRunner.cs",
                ["Helper"] = "src/Helper.cs"
            });
        var service = new ImpactAnalysisService(db, new GraphQueryService(db));

        var report = await service.ReportAsync(Repo, "A");

        var test = report.Items.Single(i => i.Key == "TestRunner");
        Assert.Equal("test", test.Classification);
        Assert.False(string.IsNullOrWhiteSpace(test.Reason));
        var other = report.Items.Single(i => i.Key == "Helper");
        Assert.Equal("other", other.Classification);
    }

    [Fact]
    public async Task Classifies_api_contract_participants()
    {
        using var db = CreateDb();
        var snap = Snapshot("s1", 0);
        await SeedAsync(db, snap, new[]
        {
            Edge("OrdersController", "A"), Edge("Subscriber", "A")
        });
        db.GraphEdges.Add(Edge("OrdersController", "OrdersApi", EdgeType.InvokesEndpoint, snap));
        db.GraphEdges.Add(Edge("Subscriber", "OrderQueue", EdgeType.Consumes, snap));
        db.KnowledgeNodes.Add(Node(snap.Id, "OrdersApi"));
        db.KnowledgeNodes.Add(Node(snap.Id, "OrderQueue"));
        await db.SaveChangesAsync();
        var service = new ImpactAnalysisService(db, new GraphQueryService(db));

        var report = await service.ReportAsync(Repo, "A");

        Assert.Equal(2, report.ByType.ApiContracts);
        Assert.Contains(report.Items, i => i.Classification == "api-contract");
    }

    [Fact]
    public async Task Classifies_database_entities_by_convention_and_dependency()
    {
        using var db = CreateDb();
        var snap = Snapshot("s1", 0);
        await SeedAsync(db, snap, new[]
        {
            Edge("OrderEntity", "A"), Edge("OrderService", "A")
        }, new Dictionary<string, string>
        {
            ["OrderEntity"] = "entities/Order.cs"
        });
        db.KnowledgeNodes.Add(Node(snap.Id, "Repository", "repositories/OrderRepository.cs"));
        db.GraphEdges.Add(Edge("OrderService", "Repository", EdgeType.Injected, snap));
        await db.SaveChangesAsync();
        var service = new ImpactAnalysisService(db, new GraphQueryService(db));

        var report = await service.ReportAsync(Repo, "A");

        Assert.Equal(2, report.ByType.DatabaseEntities);
        Assert.Contains(report.Items, i => i.Key == "OrderEntity" && i.Classification == "database-entity");
        Assert.Contains(report.Items, i => i.Key == "OrderService" && i.Classification == "database-entity");
    }

    [Fact]
    public async Task Test_path_takes_precedence_over_api_contract()
    {
        using var db = CreateDb();
        var snap = Snapshot("s1", 0);
        await SeedAsync(db, snap, new[] { Edge("OrdersApiTests", "A") });
        db.GraphEdges.Add(Edge("OrdersApiTests", "OrdersApi", EdgeType.InvokesEndpoint, snap));
        db.KnowledgeNodes.Add(Node(snap.Id, "OrdersApi"));
        var testNode = db.KnowledgeNodes.Single(n => n.SnapshotId == snap.Id && n.Key == "OrdersApiTests");
        testNode.Path = "tests/OrdersApiTests.cs";
        await db.SaveChangesAsync();
        var service = new ImpactAnalysisService(db, new GraphQueryService(db));

        var report = await service.ReportAsync(Repo, "A");

        Assert.Equal("test", report.Items.Single().Classification);
        Assert.Equal(0, report.ByType.ApiContracts);
    }

    [Fact]
    public async Task Rating_follows_documented_thresholds()
    {
        async Task<string> RatingAsync(IEnumerable<GraphEdge> edges)
        {
            using var db = CreateDb();
            await SeedAsync(db, Snapshot("s1", 0), edges);
            var service = new ImpactAnalysisService(db, new GraphQueryService(db));
            return (await service.ReportAsync(Repo, "A")).Rating;
        }

        Assert.Equal("LOW", await RatingAsync(new[] { Edge("B", "A") }));
        Assert.Equal("MEDIUM", await RatingAsync(ManyDirect(4)));
        Assert.Equal("HIGH", await RatingAsync(ManyDirect(15)));
        Assert.Equal("CRITICAL", await RatingAsync(Chain(8)));
    }

    [Fact]
    public async Task Rating_is_deterministic_across_calls()
    {
        using var db = CreateDb();
        await SeedAsync(db, Snapshot("s1", 0), ManyDirect(4));
        var service = new ImpactAnalysisService(db, new GraphQueryService(db));

        var first = await service.ReportAsync(Repo, "A");
        var second = await service.ReportAsync(Repo, "A");

        Assert.Equal(first.Rating, second.Rating);
        Assert.Equal(first.ByType, second.ByType);
    }

    [Fact]
    public async Task Empty_impact_returns_zero_counts_and_low_rating()
    {
        using var db = CreateDb();
        await SeedAsync(db, Snapshot("s1", 0), Array.Empty<GraphEdge>());
        var service = new ImpactAnalysisService(db, new GraphQueryService(db));

        var report = await service.ReportAsync(Repo, "A");

        Assert.Equal(0, report.TotalCount);
        Assert.Equal(0, report.DirectCount);
        Assert.Equal(0, report.IndirectCount);
        Assert.Equal(0, report.MaxDepth);
        Assert.Equal("LOW", report.Rating);
        Assert.Empty(report.Items);
    }

    private static IEnumerable<GraphEdge> ManyDirect(int count) =>
        Enumerable.Range(0, count).Select(i => Edge($"B{i}", "A"));

    private static IEnumerable<GraphEdge> Chain(int length) =>
        Enumerable.Range(0, length).Select(i => Edge($"L{i + 1}", i == 0 ? "A" : $"L{i}"));

    private static GraphEdge Edge(string from, string to, EdgeType type = EdgeType.Calls, Snapshot? snap = null, string? evidence = null) => new()
    {
        Id = Guid.NewGuid(), RepositoryId = Repo, FromKey = from, ToKey = to,
        Type = type, Evidence = evidence, Confidence = 1.0, IsStatic = true, SnapshotId = snap?.Id ?? Guid.Empty
    };

    private static KnowledgeNode Node(Guid snapshotId, string key, string? path = null) => new()
    {
        Id = Guid.NewGuid(), RepositoryId = Repo, SnapshotId = snapshotId, Key = key,
        Path = path ?? $"{key}.cs", Symbol = key, Kind = NodeKind.Class, Language = "csharp",
        StructuralHash = $"h-{key}", SemanticHash = $"s-{key}", Content = $"# {key}",
        StartLine = 1, CommitSha = "", AnalyzedAt = DateTimeOffset.UtcNow
    };

    private static Snapshot Snapshot(string sha, int seq) => new()
    {
        Id = Guid.NewGuid(), RepositoryId = Repo, CommitSha = sha,
        RootHash = $"root-{sha}", NodeCount = seq, EdgeCount = seq, CreatedAt = DateTimeOffset.UtcNow
    };

    private static async Task SeedAsync(
        TesseraDbContext db,
        Snapshot snapshot,
        IEnumerable<GraphEdge> edges,
        Dictionary<string, string>? paths = null)
    {
        db.Snapshots.Add(snapshot);
        foreach (var edge in edges)
        {
            edge.SnapshotId = snapshot.Id;
            db.GraphEdges.Add(edge);
        }
        foreach (var key in edges.SelectMany(e => new[] { e.FromKey, e.ToKey }).Distinct())
        {
            db.KnowledgeNodes.Add(Node(snapshot.Id, key, paths?.GetValueOrDefault(key)));
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

public sealed class ImpactEndpointTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private const string AdminKey = "test-admin-key";

    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly Guid _dbName;

    public ImpactEndpointTests(WebApplicationFactory<Program> factory)
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
    public async Task Impact_returns_report_for_accessible_repository()
    {
        var id = await SeedRepoAsync();
        var token = await SeedUserAsync("bob");
        await using (var db = CreateDb())
        {
            var user = await db.GitHubUsers.SingleAsync(u => u.Login == "bob");
            user.InstallationIdsJson = "[7]";
            await db.SaveChangesAsync();
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/repositories/{id}/impact?entity=Order&maxDepth=3");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("Order", body.GetProperty("entity").GetString());
        Assert.True(body.GetProperty("totalCount").GetInt32() >= 0);
        Assert.True(body.TryGetProperty("items", out _));
    }

    [Fact]
    public async Task Impact_returns_403_without_access()
    {
        var id = await SeedRepoAsync();
        var token = await SeedUserAsync("bob");
        await using (var db = CreateDb())
        {
            var user = await db.GitHubUsers.SingleAsync(u => u.Login == "bob");
            user.InstallationIdsJson = "[]";
            await db.SaveChangesAsync();
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/repositories/{id}/impact?entity=Order");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<Guid> SeedRepoAsync()
    {
        await using var db = CreateDb();
        var repo = new Repository
        {
            Id = Guid.NewGuid(),
            GitHubId = 601,
            Owner = "acme",
            Name = "impact",
            FullName = "acme/impact",
            CloneUrl = "https://github.com/acme/impact.git",
            DefaultBranch = "main",
            InstallationId = 7,
            CreatedBy = "alice",
            IsConnected = true,
            Status = ProcessingStatus.Completed,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.Repositories.Add(repo);
        db.Snapshots.Add(new Snapshot
        {
            Id = Guid.NewGuid(), RepositoryId = repo.Id, CommitSha = "abc",
            RootHash = "root", NodeCount = 0, EdgeCount = 0, CreatedAt = DateTimeOffset.UtcNow
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
