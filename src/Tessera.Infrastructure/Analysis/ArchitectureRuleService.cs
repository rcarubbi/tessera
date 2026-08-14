using Microsoft.EntityFrameworkCore;
using Tessera.Domain.Entities;
using Tessera.Domain.Enums;
using Tessera.Infrastructure.Data;
using Tessera.Infrastructure.Queries;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Tessera.Infrastructure.Analysis;

public sealed record RuleEvaluationResult(string CommitSha, IReadOnlyList<RuleViolation> Violations);

public sealed record RuleDriftEntry(
    string RuleName,
    RuleSeverity Severity,
    string FromKey,
    string ToKey,
    string FromPath,
    string ToPath,
    EdgeType? EdgeType,
    string IntroducedCommit,
    bool IsLive,
    bool LowConfidence);

public sealed record RuleDriftResult(string FromCommit, string ToCommit, IReadOnlyList<RuleDriftEntry> Entries);

public sealed class ArchitectureRuleService(TesseraDbContext db)
{
    private const double LowConfidenceThreshold = 0.7;
    private const int DefaultDriftSnapshotRange = 25;

    private static readonly IDeserializer Deserializer =
        new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();

    public static ArchitectureRuleSet Parse(string yaml)
    {
        RuleSetDto dto;
        try
        {
            dto = Deserializer.Deserialize<RuleSetDto>(yaml ?? "");
        }
        catch (YamlException ex)
        {
            throw new ArgumentException($"Invalid YAML: {ex.Message}");
        }

        var rules = new List<ArchitectureRule>();
        foreach (var ruleDto in dto.Rules ?? [])
        {
            rules.Add(ParseRule(ruleDto));
        }
        return new ArchitectureRuleSet(rules);
    }

    public async Task<RuleEvaluationResult> EvaluateAsync(
        Guid repositoryId,
        ArchitectureRuleSet ruleSet,
        string? commitSha = null,
        CancellationToken ct = default)
    {
        var snapshot = await ResolveSnapshotAsync(repositoryId, commitSha, ct);
        var nodes = await NodesByKeyAsync(snapshot.Id, ct);
        var edges = await EdgesAsync(snapshot.Id, ct);
        return new RuleEvaluationResult(snapshot.CommitSha, Evaluate(ruleSet, nodes, edges));
    }

    public async Task<RuleDriftResult> DriftAsync(
        Guid repositoryId,
        ArchitectureRuleSet ruleSet,
        string? fromCommit = null,
        string? toCommit = null,
        CancellationToken ct = default)
    {
        var snapshots = await ResolveSnapshotRangeAsync(repositoryId, fromCommit, toCommit, ct);
        if (snapshots.Count == 0)
        {
            throw new SnapshotNotFoundException(repositoryId, fromCommit ?? toCommit);
        }

        var historyByEdgeKey = await LoadEdgeHistoryAsync(repositoryId, ct);

        var firstSeen = new Dictionary<string, (int Index, RuleViolation Violation)>(StringComparer.Ordinal);
        var seenAtLatest = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < snapshots.Count; i++)
        {
            var snapshot = snapshots[i];
            var nodes = await NodesByKeyAsync(snapshot.Id, ct);
            var edges = await EdgesAsync(snapshot.Id, ct);
            var violations = Evaluate(ruleSet, nodes, edges);
            foreach (var violation in violations)
            {
                var key = ViolationKey(violation);
                if (!firstSeen.ContainsKey(key))
                {
                    firstSeen[key] = (i, violation);
                }
                if (i == snapshots.Count - 1)
                {
                    seenAtLatest.Add(key);
                }
            }
        }

        var entries = firstSeen
            .OrderBy(kv => kv.Value.Index)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv =>
            {
                var (index, violation) = kv.Value;
                var introducedCommit = ResolveIntroducedCommit(violation, snapshots[index], historyByEdgeKey);
                return new RuleDriftEntry(
                    violation.RuleName,
                    violation.Severity,
                    violation.FromKey,
                    violation.ToKey,
                    violation.FromPath,
                    violation.ToPath,
                    violation.EdgeType,
                    introducedCommit,
                    seenAtLatest.Contains(kv.Key),
                    violation.LowConfidence);
            })
            .ToList();

        return new RuleDriftResult(snapshots[0].CommitSha, snapshots[^1].CommitSha, entries);
    }

    private static ArchitectureRule ParseRule(RuleDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
        {
            throw new ArgumentException("Rule name is required.");
        }

        var severity = RuleSeverity.Warning;
        if (!string.IsNullOrWhiteSpace(dto.Severity))
        {
            if (!Enum.TryParse(dto.Severity, ignoreCase: true, out severity))
            {
                throw new ArgumentException($"Rule '{dto.Name}': severity must be one of info, warning, error.");
            }
        }

        var hasDeny = dto.Deny is not null;
        var hasRequire = dto.Require is not null;
        if (hasDeny == hasRequire)
        {
            throw new ArgumentException($"Rule '{dto.Name}': must have exactly one of 'deny' or 'require'.");
        }

        var constraintDto = hasDeny ? dto.Deny! : dto.Require!;
        var from = ParseSelector(constraintDto.From, dto.Name);
        if (from.IsEmpty)
        {
            throw new ArgumentException($"Rule '{dto.Name}': 'from' selector must include a path prefix and/or kind.");
        }
        if (hasDeny && constraintDto.To is null)
        {
            throw new ArgumentException($"Rule '{dto.Name}': deny constraint requires a 'to' selector.");
        }
        var to = constraintDto.To is null ? null : ParseSelector(constraintDto.To, dto.Name);
        if (to?.IsEmpty == true)
        {
            throw new ArgumentException($"Rule '{dto.Name}': 'to' selector must include a path prefix and/or kind.");
        }

        return new ArchitectureRule(
            dto.Name.Trim(),
            severity,
            new RuleConstraint(hasDeny ? RuleConstraintKind.Deny : RuleConstraintKind.Require, from, to));
    }

    private static NodeSelector ParseSelector(SelectorDto? selector, string ruleName)
    {
        if (selector is null)
        {
            throw new ArgumentException($"Rule '{ruleName}': selector is missing.");
        }

        NodeKind? kind = null;
        if (!string.IsNullOrWhiteSpace(selector.Kind))
        {
            if (!Enum.TryParse(selector.Kind, ignoreCase: true, out NodeKind parsedKind))
            {
                throw new ArgumentException($"Rule '{ruleName}': unknown node kind '{selector.Kind}'.");
            }
            kind = parsedKind;
        }

        return new NodeSelector(string.IsNullOrWhiteSpace(selector.Path) ? null : selector.Path.Trim(), kind);
    }

    public static IReadOnlyList<RuleViolation> Evaluate(
        ArchitectureRuleSet ruleSet,
        Dictionary<string, KnowledgeNode> nodes,
        List<GraphEdge> edges)
    {
        var violations = new List<RuleViolation>();
        foreach (var rule in ruleSet.Rules)
        {
            switch (rule.Constraint.Kind)
            {
                case RuleConstraintKind.Deny:
                    foreach (var edge in edges)
                    {
                        if (!nodes.TryGetValue(edge.FromKey, out var fromNode)
                            || !Matches(fromNode, rule.Constraint.From))
                        {
                            continue;
                        }
                        if (rule.Constraint.To is not null
                            && (!nodes.TryGetValue(edge.ToKey, out var toNode) || !Matches(toNode, rule.Constraint.To)))
                        {
                            continue;
                        }
                        violations.Add(new RuleViolation(
                            rule.Name,
                            rule.Severity,
                            edge.FromKey,
                            edge.ToKey,
                            fromNode.Path,
                            fromNode.StartLine,
                            nodes.TryGetValue(edge.ToKey, out var target) ? target.Path : "",
                            target is not null ? target.StartLine : 0,
                            edge.Type,
                            edge.Confidence,
                            edge.Confidence < LowConfidenceThreshold));
                    }
                    break;

                case RuleConstraintKind.Require:
                    var satisfied = edges.Any(edge =>
                        nodes.TryGetValue(edge.FromKey, out var fromNode) && Matches(fromNode, rule.Constraint.From)
                        && (rule.Constraint.To is null
                            || (nodes.TryGetValue(edge.ToKey, out var toNode) && Matches(toNode, rule.Constraint.To))));
                    if (!satisfied)
                    {
                        violations.Add(new RuleViolation(
                            rule.Name,
                            rule.Severity,
                            "",
                            "",
                            "",
                            0,
                            "",
                            0,
                            null,
                            1.0,
                            false));
                    }
                    break;
            }
        }
        return violations;
    }

    private static bool Matches(KnowledgeNode node, NodeSelector selector)
        => (selector.PathPrefix is null
                || node.Path.StartsWith(selector.PathPrefix, StringComparison.OrdinalIgnoreCase))
            && (selector.Kind is null || node.Kind == selector.Kind);

    private async Task<Snapshot> ResolveSnapshotAsync(Guid repositoryId, string? commitSha, CancellationToken ct)
    {
        var query = db.Snapshots.AsNoTracking().Where(s => s.RepositoryId == repositoryId);
        if (!string.IsNullOrEmpty(commitSha))
        {
            query = query.Where(s => s.CommitSha == commitSha);
        }
        return await query.OrderByDescending(s => s.CreatedAt).FirstOrDefaultAsync(ct)
            ?? throw new SnapshotNotFoundException(repositoryId, commitSha);
    }

    private async Task<List<Snapshot>> ResolveSnapshotRangeAsync(
        Guid repositoryId,
        string? fromCommit,
        string? toCommit,
        CancellationToken ct)
    {
        var snapshots = await db.Snapshots.AsNoTracking()
            .Where(s => s.RepositoryId == repositoryId)
            .OrderBy(s => s.CreatedAt)
            .ToListAsync(ct);

        if (fromCommit is not null)
        {
            var from = snapshots.FirstOrDefault(s => s.CommitSha == fromCommit)
                ?? throw new SnapshotNotFoundException(repositoryId, fromCommit);
            snapshots = snapshots.Where(s => s.CreatedAt >= from.CreatedAt).ToList();
        }
        if (toCommit is not null)
        {
            var to = snapshots.FirstOrDefault(s => s.CommitSha == toCommit)
                ?? throw new SnapshotNotFoundException(repositoryId, toCommit);
            snapshots = snapshots.Where(s => s.CreatedAt <= to.CreatedAt).ToList();
        }

        return snapshots.TakeLast(DefaultDriftSnapshotRange).ToList();
    }

    private async Task<Dictionary<string, IReadOnlyList<EdgeHistory>>> LoadEdgeHistoryAsync(Guid repositoryId, CancellationToken ct)
    {
        var rows = await db.EdgeHistories.AsNoTracking()
            .Where(h => h.RepositoryId == repositoryId)
            .OrderBy(h => h.IntroducedAt)
            .ToListAsync(ct);
        return rows.GroupBy(Key).ToDictionary(g => g.Key, g => (IReadOnlyList<EdgeHistory>)g.ToList(), StringComparer.Ordinal);
    }

    private static string ResolveIntroducedCommit(
        RuleViolation violation,
        Snapshot firstSeen,
        IReadOnlyDictionary<string, IReadOnlyList<EdgeHistory>> historyByEdgeKey)
    {
        if (violation.IsMissingRequirement || violation.EdgeType is null)
        {
            return firstSeen.CommitSha;
        }
        if (historyByEdgeKey.TryGetValue(Key(violation), out var rows))
        {
            var row = rows.LastOrDefault(h => h.IntroducedAt <= firstSeen.CreatedAt);
            if (row is not null)
            {
                return row.IntroducedCommitSha;
            }
        }
        return firstSeen.CommitSha;
    }

    private static string ViolationKey(RuleViolation violation)
        => violation.IsMissingRequirement
            ? violation.RuleName
            : $"{violation.RuleName}|{violation.FromKey}|{violation.ToKey}|{violation.EdgeType}";

    private static string Key(EdgeHistory history) => $"{history.FromKey}|{history.ToKey}|{history.Type}";

    private static string Key(RuleViolation violation) => $"{violation.FromKey}|{violation.ToKey}|{violation.EdgeType}";

    private Task<Dictionary<string, KnowledgeNode>> NodesByKeyAsync(Guid snapshotId, CancellationToken ct) =>
        db.KnowledgeNodes.AsNoTracking()
            .Where(n => n.SnapshotId == snapshotId)
            .ToDictionaryAsync(n => n.Key, StringComparer.Ordinal, ct);

    private Task<List<GraphEdge>> EdgesAsync(Guid snapshotId, CancellationToken ct) =>
        db.GraphEdges.AsNoTracking()
            .Where(e => e.SnapshotId == snapshotId)
            .ToListAsync(ct);

    private sealed class RuleSetDto
    {
        public List<RuleDto>? Rules { get; set; }
    }

    private sealed class RuleDto
    {
        public string? Name { get; set; }
        public string? Severity { get; set; }
        public ConstraintDto? Deny { get; set; }
        public ConstraintDto? Require { get; set; }
    }

    private sealed class ConstraintDto
    {
        public SelectorDto? From { get; set; }
        public SelectorDto? To { get; set; }
    }

    private sealed class SelectorDto
    {
        public string? Path { get; set; }
        public string? Kind { get; set; }
    }
}
