using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Tessera.Domain.Entities;
using Tessera.Domain.Enums;
using Tessera.Infrastructure.Analysis;
using Tessera.Infrastructure.Data;

namespace Tessera.Integration.Tests;

public sealed class ArchitectureRuleTests
{
    private static readonly Guid Repo = Guid.NewGuid();

    private const string DenyYaml = """
        rules:
          - name: "Domain must not depend on Infrastructure"
            severity: error
            deny:
              from: { path: "src/Tessera.Domain" }
              to: { path: "src/Tessera.Infrastructure" }
        """;

    private const string RequireYaml = """
        rules:
          - name: "Services must use interfaces"
            severity: warning
            require:
              from: { kind: "Class" }
              to: { kind: "Interface" }
        """;

    [Fact]
    public void Parse_rejects_rule_without_name()
    {
        var yaml = """
            rules:
              - severity: error
                deny:
                  from: { path: "A" }
                  to: { path: "B" }
            """;

        var ex = Assert.Throws<ArgumentException>(() => ArchitectureRuleService.Parse(yaml));
        Assert.Contains("name", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_rejects_rule_without_constraint()
    {
        var yaml = """
            rules:
              - name: "No constraint"
                severity: error
            """;

        var ex = Assert.Throws<ArgumentException>(() => ArchitectureRuleService.Parse(yaml));
        Assert.Contains("deny", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_rejects_unknown_node_kind()
    {
        var yaml = """
            rules:
              - name: "Bad kind"
                deny:
                  from: { kind: "Controller" }
                  to: { path: "B" }
            """;

        var ex = Assert.Throws<ArgumentException>(() => ArchitectureRuleService.Parse(yaml));
        Assert.Contains("kind", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_rejects_unknown_severity()
    {
        var yaml = """
            rules:
              - name: "Bad severity"
                severity: critical
                deny:
                  from: { path: "A" }
                  to: { path: "B" }
            """;

        var ex = Assert.Throws<ArgumentException>(() => ArchitectureRuleService.Parse(yaml));
        Assert.Contains("severity", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_rejects_invalid_yaml()
    {
        var ex = Assert.Throws<ArgumentException>(() => ArchitectureRuleService.Parse("rules: [unclosed"));
        Assert.Contains("YAML", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_accepts_valid_rules_with_defaults()
    {
        var ruleSet = ArchitectureRuleService.Parse(DenyYaml);

        var rule = Assert.Single(ruleSet.Rules);
        Assert.Equal("Domain must not depend on Infrastructure", rule.Name);
        Assert.Equal(RuleSeverity.Error, rule.Severity);
        Assert.Equal(RuleConstraintKind.Deny, rule.Constraint.Kind);
        Assert.Equal("src/Tessera.Domain", rule.Constraint.From.PathPrefix);
        Assert.Null(rule.Constraint.From.Kind);
        Assert.Equal("src/Tessera.Infrastructure", rule.Constraint.To!.PathPrefix);
    }

    [Fact]
    public void Parse_defaults_severity_to_warning()
    {
        var yaml = """
            rules:
              - name: "Implicit warning"
                deny:
                  from: { path: "A" }
                  to: { path: "B" }
            """;

        var rule = Assert.Single(ArchitectureRuleService.Parse(yaml).Rules);
        Assert.Equal(RuleSeverity.Warning, rule.Severity);
    }

    [Fact]
    public async Task Evaluate_reports_deny_violations_with_paths_and_lines()
    {
        using var db = CreateDb();
        var snapshot = SeedSnapshot(db, "s1",
            Node("A", "src/Tessera.Domain/A.cs", NodeKind.Class, 10),
            Node("B", "src/Tessera.Infrastructure/B.cs", NodeKind.Class, 20),
            Node("C", "src/Tessera.Api/C.cs", NodeKind.Class, 30));
        AddEdges(db, snapshot.Id, Edge("A", "B", EdgeType.Calls), Edge("A", "C", EdgeType.Calls));

        var service = new ArchitectureRuleService(db);
        var result = await service.EvaluateAsync(Repo, ArchitectureRuleService.Parse(DenyYaml), "s1");

        Assert.Equal("s1", result.CommitSha);
        var violation = Assert.Single(result.Violations);
        Assert.Equal("Domain must not depend on Infrastructure", violation.RuleName);
        Assert.Equal(RuleSeverity.Error, violation.Severity);
        Assert.Equal("A", violation.FromKey);
        Assert.Equal("B", violation.ToKey);
        Assert.Equal("src/Tessera.Domain/A.cs", violation.FromPath);
        Assert.Equal(10, violation.FromLine);
        Assert.Equal("src/Tessera.Infrastructure/B.cs", violation.ToPath);
        Assert.Equal(20, violation.ToLine);
        Assert.Equal(EdgeType.Calls, violation.EdgeType);
        Assert.False(violation.LowConfidence);
    }

    [Fact]
    public async Task Evaluate_flags_low_confidence_edges()
    {
        using var db = CreateDb();
        var snapshot = SeedSnapshot(db, "s1",
            Node("A", "src/Tessera.Domain/A.cs", NodeKind.Class, 10),
            Node("B", "src/Tessera.Infrastructure/B.cs", NodeKind.Class, 20));
        AddEdges(db, snapshot.Id, Edge("A", "B", EdgeType.Calls, confidence: 0.6));

        var service = new ArchitectureRuleService(db);
        var result = await service.EvaluateAsync(Repo, ArchitectureRuleService.Parse(DenyYaml), "s1");

        var violation = Assert.Single(result.Violations);
        Assert.Equal(0.6, violation.Confidence);
        Assert.True(violation.LowConfidence);
    }

    [Fact]
    public async Task Evaluate_matches_selector_by_kind()
    {
        using var db = CreateDb();
        var snapshot = SeedSnapshot(db, "s1",
            Node("Impl", "src/app/Impl.cs", NodeKind.Class, 1),
            Node("Svc", "src/app/Svc.cs", NodeKind.Interface, 2));
        AddEdges(db, snapshot.Id, Edge("Impl", "Svc", EdgeType.Calls));

        var service = new ArchitectureRuleService(db);
        var result = await service.EvaluateAsync(Repo, ArchitectureRuleService.Parse(RequireYaml), "s1");

        Assert.Empty(result.Violations);
    }

    [Fact]
    public async Task Evaluate_reports_missing_requirement()
    {
        using var db = CreateDb();
        var snapshot = SeedSnapshot(db, "s1",
            Node("Impl", "src/app/Impl.cs", NodeKind.Class, 1),
            Node("Svc", "src/app/Svc.cs", NodeKind.Class, 2));
        AddEdges(db, snapshot.Id, Edge("Impl", "Svc", EdgeType.Calls));

        var service = new ArchitectureRuleService(db);
        var result = await service.EvaluateAsync(Repo, ArchitectureRuleService.Parse(RequireYaml), "s1");

        var violation = Assert.Single(result.Violations);
        Assert.True(violation.IsMissingRequirement);
        Assert.Equal(RuleSeverity.Warning, violation.Severity);
        Assert.Null(violation.EdgeType);
    }

    [Fact]
    public async Task Drift_reports_introduction_commit_and_resolved_flag_via_edge_history()
    {
        using var db = CreateDb();
        var s1 = SeedSnapshot(db, "s1",
            Node("A", "src/Tessera.Domain/A.cs", NodeKind.Class, 10),
            Node("B", "src/Tessera.Infrastructure/B.cs", NodeKind.Class, 20));
        AddEdges(db, s1.Id, Edge("A", "B", EdgeType.Calls));
        await EdgeHistoryUpdater.UpdateAsync(db, Repo, s1.Id, "s1", db.GraphEdges.Where(e => e.SnapshotId == s1.Id).ToList(), null);
        await db.SaveChangesAsync();

        var s2 = SeedSnapshot(db, "s2",
            Node("A", "src/Tessera.Domain/A.cs", NodeKind.Class, 10),
            Node("B", "src/Tessera.Infrastructure/B.cs", NodeKind.Class, 20));
        await EdgeHistoryUpdater.UpdateAsync(db, Repo, s2.Id, "s2", Array.Empty<GraphEdge>(), s1.Id);
        await db.SaveChangesAsync();

        var service = new ArchitectureRuleService(db);
        var drift = await service.DriftAsync(Repo, ArchitectureRuleService.Parse(DenyYaml));

        var entry = Assert.Single(drift.Entries);
        Assert.Equal("s1", entry.IntroducedCommit);
        Assert.False(entry.IsLive);
    }

    [Fact]
    public async Task Drift_flags_reintroduced_violation_live_with_original_introduction_commit()
    {
        using var db = CreateDb();
        var s1 = SeedSnapshot(db, "s1",
            Node("A", "src/Tessera.Domain/A.cs", NodeKind.Class, 10),
            Node("B", "src/Tessera.Infrastructure/B.cs", NodeKind.Class, 20));
        AddEdges(db, s1.Id, Edge("A", "B", EdgeType.Calls));
        await EdgeHistoryUpdater.UpdateAsync(db, Repo, s1.Id, "s1", db.GraphEdges.Where(e => e.SnapshotId == s1.Id).ToList(), null);
        await db.SaveChangesAsync();

        var s2 = SeedSnapshot(db, "s2",
            Node("A", "src/Tessera.Domain/A.cs", NodeKind.Class, 10),
            Node("B", "src/Tessera.Infrastructure/B.cs", NodeKind.Class, 20));
        await EdgeHistoryUpdater.UpdateAsync(db, Repo, s2.Id, "s2", Array.Empty<GraphEdge>(), s1.Id);
        await db.SaveChangesAsync();

        var s3 = SeedSnapshot(db, "s3",
            Node("A", "src/Tessera.Domain/A.cs", NodeKind.Class, 10),
            Node("B", "src/Tessera.Infrastructure/B.cs", NodeKind.Class, 20));
        AddEdges(db, s3.Id, Edge("A", "B", EdgeType.Calls));
        await EdgeHistoryUpdater.UpdateAsync(db, Repo, s3.Id, "s3", db.GraphEdges.Where(e => e.SnapshotId == s3.Id).ToList(), s2.Id);
        await db.SaveChangesAsync();

        var service = new ArchitectureRuleService(db);
        var drift = await service.DriftAsync(Repo, ArchitectureRuleService.Parse(DenyYaml));

        var entry = Assert.Single(drift.Entries);
        Assert.Equal("s1", entry.IntroducedCommit);
        Assert.True(entry.IsLive);
    }

    [Fact]
    public async Task Drift_resolves_introduction_from_edge_history_before_bounded_range()
    {
        using var db = CreateDb();
        var s1 = SeedSnapshot(db, "s1",
            Node("A", "src/Tessera.Domain/A.cs", NodeKind.Class, 10),
            Node("B", "src/Tessera.Infrastructure/B.cs", NodeKind.Class, 20));
        AddEdges(db, s1.Id, Edge("A", "B", EdgeType.Calls));
        await EdgeHistoryUpdater.UpdateAsync(db, Repo, s1.Id, "s1", db.GraphEdges.Where(e => e.SnapshotId == s1.Id).ToList(), null);
        await db.SaveChangesAsync();

        var s2 = SeedSnapshot(db, "s2",
            Node("A", "src/Tessera.Domain/A.cs", NodeKind.Class, 10),
            Node("B", "src/Tessera.Infrastructure/B.cs", NodeKind.Class, 20));
        AddEdges(db, s2.Id, Edge("A", "B", EdgeType.Calls));
        await EdgeHistoryUpdater.UpdateAsync(db, Repo, s2.Id, "s2", db.GraphEdges.Where(e => e.SnapshotId == s2.Id).ToList(), s1.Id);
        await db.SaveChangesAsync();

        var s3 = SeedSnapshot(db, "s3",
            Node("A", "src/Tessera.Domain/A.cs", NodeKind.Class, 10),
            Node("B", "src/Tessera.Infrastructure/B.cs", NodeKind.Class, 20));
        AddEdges(db, s3.Id, Edge("A", "B", EdgeType.Calls));
        await EdgeHistoryUpdater.UpdateAsync(db, Repo, s3.Id, "s3", db.GraphEdges.Where(e => e.SnapshotId == s3.Id).ToList(), s2.Id);
        await db.SaveChangesAsync();

        var service = new ArchitectureRuleService(db);
        var drift = await service.DriftAsync(Repo, ArchitectureRuleService.Parse(DenyYaml), fromCommit: "s3");

        Assert.Equal("s3", drift.FromCommit);
        var entry = Assert.Single(drift.Entries);
        Assert.Equal("s1", entry.IntroducedCommit);
        Assert.True(entry.IsLive);
    }

    [Fact]
    public async Task Drift_falls_back_to_walk_when_edge_history_is_empty()
    {
        using var db = CreateDb();
        var s1 = SeedSnapshot(db, "s1",
            Node("A", "src/Tessera.Domain/A.cs", NodeKind.Class, 10),
            Node("B", "src/Tessera.Infrastructure/B.cs", NodeKind.Class, 20));
        AddEdges(db, s1.Id, Edge("A", "B", EdgeType.Calls));

        SeedSnapshot(db, "s2",
            Node("A", "src/Tessera.Domain/A.cs", NodeKind.Class, 10),
            Node("B", "src/Tessera.Infrastructure/B.cs", NodeKind.Class, 20));

        var service = new ArchitectureRuleService(db);
        var drift = await service.DriftAsync(Repo, ArchitectureRuleService.Parse(DenyYaml));

        var entry = Assert.Single(drift.Entries);
        Assert.Equal("s1", entry.IntroducedCommit);
        Assert.False(entry.IsLive);
    }

    [Fact]
    public async Task Drift_handles_missing_requirement_using_walk()
    {
        using var db = CreateDb();
        SeedSnapshot(db, "s1",
            Node("Impl", "src/app/Impl.cs", NodeKind.Class, 1),
            Node("Svc", "src/app/Svc.cs", NodeKind.Class, 2));
        db.SaveChanges();

        var service = new ArchitectureRuleService(db);
        var drift = await service.DriftAsync(Repo, ArchitectureRuleService.Parse(RequireYaml));

        var entry = Assert.Single(drift.Entries);
        Assert.True(entry.IsLive);
        Assert.Equal("s1", entry.IntroducedCommit);
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

    private static GraphEdge Edge(string from, string to, EdgeType type = EdgeType.Calls, double confidence = 1.0) => new()
    {
        Id = Guid.NewGuid(),
        RepositoryId = Repo,
        FromKey = from,
        ToKey = to,
        Type = type,
        Confidence = confidence,
        IsStatic = true
    };

    private static void AddEdges(TesseraDbContext db, Guid snapshotId, params GraphEdge[] edges)
    {
        foreach (var edge in edges)
        {
            edge.SnapshotId = snapshotId;
            db.GraphEdges.Add(edge);
        }
        db.SaveChanges();
    }

    private static Snapshot SeedSnapshot(TesseraDbContext db, string sha, params KnowledgeNode[] nodes)
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
        foreach (var node in nodes)
        {
            node.SnapshotId = snapshot.Id;
            db.KnowledgeNodes.Add(node);
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
