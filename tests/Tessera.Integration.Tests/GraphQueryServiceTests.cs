using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Tessera.Domain.Entities;
using Tessera.Domain.Enums;
using Tessera.Infrastructure.Data;
using Tessera.Infrastructure.Queries;

namespace Tessera.Integration.Tests;

public sealed class GraphQueryServiceTests
{
    private static readonly Guid Repo = Guid.NewGuid();

    [Fact]
    public async Task Impact_returns_direct_and_transitive_dependents_with_paths()
    {
        using var db = CreateDb();
        await SeedAsync(db, Snapshot("s1", 0), new[]
        {
            Edge("B", "A"), Edge("C", "B"), Edge("D", "A"), Edge("E", "D")
        });
        var service = new GraphQueryService(db);

        var result = await service.ImpactAsync(Repo, "A");

        Assert.Equal(4, result.Items.Count);
        var direct = result.Items.Where(i => i.Severity == "direct").Select(i => i.Key).ToHashSet();
        Assert.Equal(new[] { "B", "D" }, direct.OrderBy(k => k).ToArray());
        var c = result.Items.Single(i => i.Key == "C");
        Assert.Equal(2, c.Depth);
        Assert.Equal(new[] { "A", "B", "C" }, c.Trace);
        Assert.Equal("indirect", c.Severity);
    }

    [Fact]
    public async Task Consumers_returns_reverse_edges_with_evidence()
    {
        using var db = CreateDb();
        await SeedAsync(db, Snapshot("s1", 0), new[]
        {
            Edge("B", "A", evidence: "b.cs:10"), Edge("C", "A", evidence: "c.cs:4")
        });
        var service = new GraphQueryService(db);

        var result = await service.ConsumersAsync(Repo, "A");

        Assert.Equal(2, result.Items.Count);
        Assert.Contains(result.Items, i => i.FromKey == "B" && i.Evidence == "b.cs:10" && i.Confidence == 1.0);
    }

    [Fact]
    public async Task Chain_returns_outgoing_dependencies()
    {
        using var db = CreateDb();
        await SeedAsync(db, Snapshot("s1", 0), new[]
        {
            Edge("Controller", "Service"), Edge("Service", "Repo"), Edge("Repo", "Db")
        });
        var service = new GraphQueryService(db);

        var result = await service.ChainAsync(Repo, "Controller");

        Assert.Equal(new[] { "Service", "Repo", "Db" }, result.Items.OrderBy(i => i.Depth).Select(i => i.Key).ToArray());
        Assert.Equal(3, result.Items.Last().Depth);
    }

    [Fact]
    public async Task Diff_detects_added_changed_and_new_cycle()
    {
        using var db = CreateDb();
        var from = Snapshot("s1", 1);
        var to = Snapshot("s2", 2);
        await SeedAsync(db, from, new[]
        {
            Edge("A", "B"), Edge("B", "C")
        });
        await SeedAsync(db, to, new[]
        {
            Edge("A", "B"), Edge("B", "C"), Edge("C", "A"), Edge("D", "B")
        });
        var nodeC = db.KnowledgeNodes.Single(n => n.SnapshotId == to.Id && n.Key == "C");
        nodeC.SemanticHash = "CHANGED";
        nodeC.Content = "## C";
        await db.SaveChangesAsync();

        var service = new GraphQueryService(db);
        var diff = await service.DiffAsync(Repo, "s1", "s2");

        Assert.Contains(diff.Nodes, n => n.Key == "D" && n.Change == "added");
        Assert.Contains(diff.Nodes, n => n.Key == "C" && n.Change == "changed");
        Assert.Contains(diff.Edges, e => e.Change == "added" && e.From == "C" && e.To == "A");
        Assert.True(diff.Cycles.Count > 0, $"cycles={diff.Cycles.Count} edges={string.Join(",", diff.Edges.Select(e => $"{e.Change}:{e.From}->{e.To}"))}");
        Assert.Single(diff.Cycles);
        Assert.Equal(new[] { "A", "B", "C", "A" }, diff.Cycles[0].Path);
    }

    [Fact]
    public async Task Mermaid_exports_depth_limited_subgraph()
    {
        using var db = CreateDb();
        await SeedAsync(db, Snapshot("s1", 0), new[]
        {
            Edge("B", "A"), Edge("D", "A"), Edge("C", "B")
        });
        var service = new GraphQueryService(db);

        var mermaid = await service.MermaidAsync(Repo, entityKey: "A", maxDepth: 1);

        Assert.StartsWith("flowchart LR", mermaid);
        Assert.Contains("\"B\" -->|Calls| \"A\"", mermaid);
        Assert.DoesNotContain("\"C\"", mermaid);
    }

    [Fact]
    public async Task Query_scopes_to_specific_snapshot()
    {
        using var db = CreateDb();
        await SeedAsync(db, Snapshot("s1", 0), new[] { Edge("B", "A") });
        await SeedAsync(db, Snapshot("s2", 1), new[] { Edge("B", "A"), Edge("C", "A") });
        var service = new GraphQueryService(db);

        var latest = await service.ConsumersAsync(Repo, "A");
        var historical = await service.ConsumersAsync(Repo, "A", "s1");

        Assert.Equal(2, latest.Items.Count);
        Assert.Single(historical.Items);
    }

    [Fact]
    public async Task Unknown_snapshot_throws()
    {
        using var db = CreateDb();
        await SeedAsync(db, Snapshot("s1", 0), Array.Empty<GraphEdge>());
        var service = new GraphQueryService(db);

        await Assert.ThrowsAsync<SnapshotNotFoundException>(() => service.ConsumersAsync(Repo, "A", "nope"));
    }

    [Fact]
    public void FindCycles_detects_cycle_in_full_graph()
    {
        var edges = new[]
        {
            Edge("A", "B"), Edge("B", "C"), Edge("C", "A"), Edge("D", "B")
        };

        var cycles = GraphQueryService.FindCycles(edges.ToList());

        Assert.Single(cycles);
        Assert.Equal(new[] { "A", "B", "C", "A" }, cycles[0].Path);
    }

    [Fact]
    public void FindNewCycles_reports_cycle_that_touches_an_added_edge()
    {
        var toEdges = new[] { Edge("A", "B"), Edge("B", "C"), Edge("C", "A"), Edge("D", "B") }.ToList();
        var fromEdgeSet = new HashSet<string>(StringComparer.Ordinal) { "A|B|Calls", "B|C|Calls" };

        var cycles = GraphQueryService.FindNewCycles(toEdges, fromEdgeSet);

        Assert.Single(cycles);
        Assert.Equal(new[] { "A", "B", "C", "A" }, cycles[0].Path);
    }

    [Fact]
    public async Task Graph_exports_nodes_and_edges_for_snapshot()
    {
        using var db = CreateDb();
        await SeedAsync(db, Snapshot("s1", 0), new[] { Edge("B", "A"), Edge("C", "B") });
        var service = new GraphQueryService(db);

        var graph = await service.GraphAsync(Repo);

        Assert.Equal("s1", graph.CommitSha);
        Assert.Equal(3, graph.Nodes.Count);
        var a = graph.Nodes.Single(n => n.Key == "A");
        Assert.Equal("Class", a.Kind);
        Assert.Equal("# A", a.Content);
        Assert.Equal("none", a.ReviewStatus);
        Assert.Contains(graph.Edges, e => e.From == "B" && e.To == "A" && e.Type == "Calls" && e.IsStatic);
    }

    [Fact]
    public async Task Graph_entity_filter_limits_subgraph()
    {
        using var db = CreateDb();
        await SeedAsync(db, Snapshot("s1", 0), new[] { Edge("B", "A"), Edge("C", "B") });
        var service = new GraphQueryService(db);

        var graph = await service.GraphAsync(Repo, entityKey: "A", maxDepth: 1);

        Assert.Equal(new[] { "A", "B" }, graph.Nodes.Select(n => n.Key).OrderBy(k => k).ToArray());
    }

    private static GraphEdge Edge(string from, string to, string? evidence = null) => new()
    {
        Id = Guid.NewGuid(), RepositoryId = Repo, FromKey = from, ToKey = to,
        Type = EdgeType.Calls, Evidence = evidence, Confidence = 1.0, IsStatic = true
    };

    private static KnowledgeNode Node(Guid snapshotId, string key) => new()
    {
        Id = Guid.NewGuid(), RepositoryId = Repo, SnapshotId = snapshotId, Key = key,
        Path = $"{key}.cs", Symbol = key, Kind = NodeKind.Class, Language = "csharp",
        StructuralHash = $"h-{key}", SemanticHash = $"s-{key}", Content = $"# {key}",
        CommitSha = "", AnalyzedAt = DateTimeOffset.UtcNow
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
            db.KnowledgeNodes.Add(Node(snapshot.Id, key));
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
