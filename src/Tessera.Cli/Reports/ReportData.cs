using System.Text.Json;
using Tessera.Domain.Entities;
using Tessera.Domain.Merkle;
using Tessera.Infrastructure.Queries;

namespace Tessera.Cli.Reports;

public sealed record ReportNodeData(
    string Key,
    string Symbol,
    string Kind,
    string Language,
    string Path,
    int Line,
    int EndLine,
    double Confidence,
    string? Model);

public sealed record ReportEdgeData(
    string From,
    string To,
    string Type,
    string? Evidence,
    double Confidence,
    bool IsStatic,
    string Classification,
    string FactSource,
    string Tier);

public sealed record ReportCycleData(IReadOnlyList<string> Path);

public sealed record ReportDependencyData(string Key, string Symbol, string Path, int Line, int Count);

public sealed record ReportImpactData(string Entity, IReadOnlyList<ImpactItem> Items);

public sealed record ReportData(
    string CommitSha,
    DateTimeOffset AnalyzedAt,
    int NodeCount,
    int EdgeCount,
    IReadOnlyList<ReportNodeData> Nodes,
    IReadOnlyList<ReportEdgeData> Edges,
    IReadOnlyList<ReportCycleData> Cycles,
    IReadOnlyList<ReportDependencyData> TopDependencies,
    IReadOnlyList<ReportImpactData> Impact)
{
    public static ReportData Create(
        string commitSha,
        ComposedSnapshot composed,
        int impactCount = 10,
        int dependencyCount = 20,
        int maxImpactDepth = 10)
    {
        var nodesByKey = composed.Nodes.ToDictionary(n => n.Key, StringComparer.Ordinal);

        var nodeData = composed.Nodes
            .OrderBy(n => n.Key, StringComparer.Ordinal)
            .Select(n => new ReportNodeData(
                n.Key, n.Symbol, n.Kind.ToString(), n.Language, n.Path, n.StartLine, n.EndLine, n.Confidence, n.Model))
            .ToList();

        var edgeData = composed.Edges
            .OrderBy(e => e.FromKey, StringComparer.Ordinal)
            .ThenBy(e => e.ToKey, StringComparer.Ordinal)
            .Select(e =>
            {
                var evidence = EvidenceClassifier.ClassifyEdge(e);
                return new ReportEdgeData(
                    e.FromKey, e.ToKey, e.Type.ToString(), e.Evidence, e.Confidence, e.IsStatic,
                    evidence.Classification, evidence.FactSource, evidence.Tier);
            })
            .ToList();

        var cycles = GraphAlgorithms.FindCycles(composed.Edges)
            .Select(c => new ReportCycleData(c.Path))
            .ToList();

        var topDependencies = GraphAlgorithms.TopDependencies(nodesByKey, composed.Edges, dependencyCount)
            .Select(d => new ReportDependencyData(d.Key, d.Symbol, d.Path, d.Line, d.Count))
            .ToList();

        var topByDegree = composed.Edges
            .SelectMany(e => new[] { e.FromKey, e.ToKey })
            .GroupBy(k => k, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .Take(impactCount)
            .Select(g => g.Key)
            .ToList();

        var impact = topByDegree
            .Select(key => new ReportImpactData(key, GraphAlgorithms.Impact(nodesByKey, composed.Edges, key, maxImpactDepth)))
            .ToList();

        return new ReportData(
            commitSha,
            DateTimeOffset.UtcNow,
            nodeData.Count,
            edgeData.Count,
            nodeData,
            edgeData,
            cycles,
            topDependencies,
            impact);
    }
}

public static class ReportJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}
