using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tessera.Domain.Entities;
using Tessera.Domain.Enums;
using Tessera.Domain.Ports;
using Tessera.Infrastructure.Ai;
using Tessera.Infrastructure.Chat;
using Tessera.Infrastructure.Data;
using Tessera.Infrastructure.Queries;

namespace Tessera.Integration.Tests;

public sealed class ExplainerServiceTests
{
    private static readonly Guid Repo = Guid.NewGuid();

    [Fact]
    public async Task Parses_sections_and_drops_unresolvable_claims()
    {
        using var db = CreateDb();
        var snapshot = Snapshot("s1");
        await SeedAsync(db, snapshot, Nodes(snapshot.Id), new[] { Edge("api/orders.ts::OrdersClient", "Order.cs::Order") });
        await SeedOverviewAsync(db, snapshot.Id, """
            ## Summary
            Order management system.

            ## Main components
            - [Order.cs::Order] Order — core domain entity
            - [Ghost.cs::Ghost] Ghost — not in snapshot

            ## Architectural notes
            - Layered: domain, application, infrastructure

            ## External systems
            - Stripe (payments)

            ## Component diagram

            ```mermaid
            flowchart LR
              n0["Order"]
            ```
            """);
        var service = CreateService(db, primary: null);

        var result = await service.BuildAsync(Repo);

        Assert.True(result.HasSnapshot);
        Assert.Equal("s1", result.CommitSha);
        Assert.Equal("Order management system.", result.Summary);
        var component = Assert.Single(result.MainComponents);
        Assert.Equal("Order.cs::Order", component.Key);
        Assert.Equal("Order", component.Symbol);
        Assert.Equal("Order.cs", component.Path);
        Assert.Equal(1, component.Line);
        Assert.Equal("Class", component.Kind);
        Assert.Equal("Domain entity", component.Role);
        Assert.Equal(new[] { "Layered: domain, application, infrastructure" }, result.ArchitecturalNotes);
        Assert.Equal(new[] { "Stripe (payments)" }, result.ExternalSystems);
        var diagram = Assert.Single(result.Diagrams);
        Assert.Contains("flowchart LR", diagram);
    }

    [Fact]
    public async Task Critical_components_ordered_by_degree_descending()
    {
        using var db = CreateDb();
        var snapshot = Snapshot("s1");
        await SeedAsync(db, snapshot, new[]
        {
            Node(snapshot.Id, "A", "A", "A.cs", NodeKind.Class, 1),
            Node(snapshot.Id, "B", "B", "B.cs", NodeKind.Class, 1),
            Node(snapshot.Id, "C", "C", "C.cs", NodeKind.Class, 1),
            Node(snapshot.Id, "D", "D", "D.cs", NodeKind.Class, 1)
        }, new[]
        {
            Edge("C", "A"), Edge("B", "A"), Edge("A", "D")
        });
        var service = CreateService(db, primary: null);

        var result = await service.BuildAsync(Repo);

        Assert.Equal(new[] { "A", "B", "C", "D" }, result.CriticalComponents.Select(c => c.Key).ToArray());
        var top = result.CriticalComponents[0];
        Assert.Equal(3, top.Centrality);
        Assert.Equal("A.cs", top.Path);
        Assert.Equal(1, top.Line);
    }

    [Fact]
    public async Task Falls_back_to_rule_based_overview_with_same_shape()
    {
        using var db = CreateDb();
        var snapshot = Snapshot("s1");
        await SeedAsync(db, snapshot, Nodes(snapshot.Id), Array.Empty<GraphEdge>());
        var service = CreateService(db, primary: null);

        var result = await service.BuildAsync(Repo);

        Assert.True(result.HasSnapshot);
        Assert.Contains("Semantic overview unavailable", result.Summary);
        Assert.Equal(2, result.MainComponents.Count);
        Assert.Equal(2, result.Diagrams.Count);
        Assert.Contains(result.Diagrams, d => d.Contains("flowchart LR") && d.Contains("Order"));
        Assert.Contains(result.Diagrams, d => d.Contains("flowchart LR") && d.Contains("OrdersClient"));
    }

    [Fact]
    public async Task Reuses_stored_overview_without_generating()
    {
        using var db = CreateDb();
        var snapshot = Snapshot("s1");
        await SeedAsync(db, snapshot, Nodes(snapshot.Id), Array.Empty<GraphEdge>());
        await SeedOverviewAsync(db, snapshot.Id, "## Summary\nStored overview.");
        var provider = new FakeChatProvider("p", "m", _ => throw new InvalidOperationException("provider must not be called"));
        var service = CreateService(db, provider);

        var result = await service.BuildAsync(Repo);

        Assert.Equal("Stored overview.", result.Summary);
        Assert.Equal("test", result.Model);
        Assert.Equal(2, result.NodeCount);
        Assert.Contains("## Summary", result.RawOverview);
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task Returns_empty_state_when_repository_has_no_snapshot()
    {
        using var db = CreateDb();
        db.Repositories.Add(RepoEntity());
        await db.SaveChangesAsync();
        var service = CreateService(db, primary: null);

        var result = await service.BuildAsync(Repo);

        Assert.False(result.HasSnapshot);
        Assert.NotNull(result.EmptyReason);
        Assert.Null(result.Summary);
        Assert.Empty(result.MainComponents);
        Assert.Empty(result.CriticalComponents);
    }

    [Fact]
    public async Task Returns_empty_state_for_unknown_commit()
    {
        using var db = CreateDb();
        var snapshot = Snapshot("s1");
        await SeedAsync(db, snapshot, Nodes(snapshot.Id), Array.Empty<GraphEdge>());
        var service = CreateService(db, primary: null);

        var result = await service.BuildAsync(Repo, "nope");

        Assert.False(result.HasSnapshot);
    }

    private static ExplainerService CreateService(TesseraDbContext db, FakeChatProvider? primary)
    {
        var overview = new OverviewService(
            new FakeProviderRegistry(primary),
            new TokenBudgetTracker(Options.Create(new AiOptions { DailyBudgetTokens = 10_000_000 })),
            Options.Create(new AiOptions { MaxRetries = 1, DailyBudgetTokens = 10_000_000 }));
        return new ExplainerService(db, overview, new GraphQueryService(db));
    }

    private static async Task SeedAsync(
        TesseraDbContext db,
        Snapshot snapshot,
        IEnumerable<KnowledgeNode> nodes,
        IEnumerable<GraphEdge> edges)
    {
        db.Repositories.Add(RepoEntity());
        db.Snapshots.Add(snapshot);
        foreach (var node in nodes)
        {
            node.SnapshotId = snapshot.Id;
            db.KnowledgeNodes.Add(node);
        }
        foreach (var edge in edges)
        {
            edge.SnapshotId = snapshot.Id;
            db.GraphEdges.Add(edge);
        }
        await db.SaveChangesAsync();
    }

    private static async Task SeedOverviewAsync(TesseraDbContext db, Guid snapshotId, string content)
    {
        db.ProjectOverviews.Add(new ProjectOverview
        {
            Id = Guid.NewGuid(),
            RepositoryId = Repo,
            SnapshotId = snapshotId,
            Content = content,
            Model = "test",
            NodeCount = 2,
            GeneratedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private static Repository RepoEntity() => new()
    {
        Id = Repo,
        GitHubId = 1,
        Owner = "test",
        Name = "repo",
        FullName = "test/repo",
        DefaultBranch = "main",
        CreatedBy = "admin",
        IsConnected = true
    };

    private static Snapshot Snapshot(string sha) => new()
    {
        Id = Guid.NewGuid(),
        RepositoryId = Repo,
        CommitSha = sha,
        RootHash = $"root-{sha}",
        CreatedAt = DateTimeOffset.UtcNow
    };

    private static IReadOnlyList<KnowledgeNode> Nodes(Guid snapshotId) =>
    [
        Node(snapshotId, "Order.cs::Order", "Order", "Order.cs", NodeKind.Class, 1, role: "Domain entity"),
        Node(snapshotId, "api/orders.ts::OrdersClient", "OrdersClient", "api/orders.ts", NodeKind.Class, 1, role: "API client")
    ];

    private static KnowledgeNode Node(
        Guid snapshotId,
        string key,
        string symbol,
        string path,
        NodeKind kind,
        int line,
        string? role = null) => new()
    {
        Id = Guid.NewGuid(),
        RepositoryId = Repo,
        SnapshotId = snapshotId,
        Key = key,
        Path = path,
        Symbol = symbol,
        Kind = kind,
        Language = "c_sharp",
        StartLine = line,
        EndLine = line + 10,
        StructuralHash = $"h-{key}",
        SemanticHash = $"s-{key}",
        Content = role is null ? $"# {symbol}" : $"- Architecture: {role}\n- Bounded context: Orders",
        Confidence = 0.9,
        AnalyzedAt = DateTimeOffset.UtcNow
    };

    private static GraphEdge Edge(string from, string to) => new()
    {
        Id = Guid.NewGuid(),
        RepositoryId = Repo,
        FromKey = from,
        ToKey = to,
        Type = EdgeType.Calls,
        Confidence = 1.0,
        IsStatic = true
    };

    private static TesseraDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<TesseraDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new TesseraDbContext(options);
    }

    private sealed class FakeProviderRegistry(FakeChatProvider? primary) : IProviderRegistry
    {
        private readonly FakeChatProvider? _primary = primary;
        public IChatProvider? Primary => _primary;
        public IChatProvider? LargeTier => null;
        public IChatProvider? Fallback => null;
        public IEmbeddingProvider? Embedding => null;
        public int Count => _primary is null ? 0 : 1;
        public IChatProvider? Get(string? name) => _primary?.Name == name ? _primary : null;
    }

    private sealed class FakeChatProvider : IChatProvider
    {
        private readonly Func<IReadOnlyList<ChatMessage>, string> _handler;
        public FakeChatProvider(string name, string model, Func<IReadOnlyList<ChatMessage>, string> handler)
        {
            Name = name;
            Model = model;
            _handler = handler;
        }

        public string Name { get; }
        public string Model { get; }
        public int Calls { get; private set; }

        public Task<string> CompleteAsync(IReadOnlyList<ChatMessage> messages, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(_handler(messages));
        }
    }
}

public sealed class ExplainerEndpointTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private const string AdminKey = "test-admin-key";

    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly Guid _dbName;

    public ExplainerEndpointTests(WebApplicationFactory<Program> factory)
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
    public async Task Explain_returns_403_for_user_without_access()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var alice = await SeedUserAsync($"alice-{suffix}");
        var bob = await SeedUserAsync($"bob-{suffix}");

        var created = await PostAsync(alice, Payload($"priv-{suffix}", $"/repos/local/priv-{suffix}"));
        var id = JsonDocument.Parse(await created.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/repositories/{id}/explain");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bob);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Explain_returns_structured_overview_for_snapshot()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var created = await AdminPostAsync(Payload($"app-{suffix}", $"/repos/local/app-{suffix}"));
        var id = JsonDocument.Parse(await created.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();

        await using (var db = CreateDb())
        {
            var snapshot = new Snapshot
            {
                Id = Guid.NewGuid(),
                RepositoryId = id,
                CommitSha = "abc123",
                RootHash = "root",
                CreatedAt = DateTimeOffset.UtcNow
            };
            db.Snapshots.Add(snapshot);
            db.KnowledgeNodes.Add(new KnowledgeNode
            {
                Id = Guid.NewGuid(),
                RepositoryId = id,
                SnapshotId = snapshot.Id,
                Key = "X.cs::X",
                Path = "X.cs",
                Symbol = "X",
                Kind = NodeKind.Class,
                Language = "c_sharp",
                StartLine = 1,
                EndLine = 10,
                StructuralHash = "h",
                SemanticHash = "s",
                Content = "- Architecture: Core service",
                AnalyzedAt = DateTimeOffset.UtcNow
            });
            db.ProjectOverviews.Add(new ProjectOverview
            {
                Id = Guid.NewGuid(),
                RepositoryId = id,
                SnapshotId = snapshot.Id,
                Content = "## Summary\nHello system.\n\n## Main components\n- [X.cs::X] X — thing",
                Model = "test",
                NodeCount = 1,
                GeneratedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/repositories/{id}/explain");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminKey);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.True(root.GetProperty("hasSnapshot").GetBoolean());
        Assert.Equal("Hello system.", root.GetProperty("summary").GetString());
        Assert.Equal("abc123", root.GetProperty("commitSha").GetString());
        Assert.Equal("test", root.GetProperty("model").GetString());
        Assert.Equal(1, root.GetProperty("nodeCount").GetInt32());
        Assert.Contains("## Summary", root.GetProperty("rawOverview").GetString());
        var components = root.GetProperty("mainComponents");
        Assert.Equal(1, components.GetArrayLength());
        Assert.Equal("X.cs::X", components[0].GetProperty("key").GetString());
    }

    private Task<HttpResponseMessage> AdminPostAsync(object body) => PostAsync(AdminKey, body);

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
