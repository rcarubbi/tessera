using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Tessera.Domain.Entities;
using Tessera.Infrastructure.Data;
using Tessera.Infrastructure.Queries;

namespace Tessera.Infrastructure.Chat;

public sealed record ExplainedComponent(string Key, string Symbol, string Path, int Line, string Kind, string Role);

public sealed record ExplainResult(
    bool HasSnapshot,
    string? EmptyReason,
    string? CommitSha,
    string? Summary,
    IReadOnlyList<string> Diagrams,
    string? RawOverview,
    string Model,
    int NodeCount,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<ExplainedComponent> MainComponents,
    IReadOnlyList<string> ArchitecturalNotes,
    IReadOnlyList<string> ExternalSystems,
    IReadOnlyList<CriticalComponent> CriticalComponents);

public sealed class ExplainerService(
    TesseraDbContext db,
    IOverviewService overviewService,
    GraphQueryService queries)
{
    private const int CriticalTop = 10;

    private static readonly Regex ComponentBulletRegex = new(
        @"^\s*[-*]\s+\[([^\]]+)\]\s*(.*)$",
        RegexOptions.Compiled);

    private static readonly Regex BulletRegex = new(
        @"^\s*[-*]\s+(.+)$",
        RegexOptions.Compiled);

    private static readonly Regex MermaidFenceRegex = new(
        @"```mermaid\s*(.*?)```",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex RoleRegex = new(
        @"(?im)^\s*[-*]\s*(?:Architecture|Role)\s*:\s*(.+)$",
        RegexOptions.Compiled);

    public async Task<ExplainResult> BuildAsync(
        Guid repositoryId,
        string? commitSha = null,
        CancellationToken ct = default)
    {
        var repo = await db.Repositories.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == repositoryId, ct);
        if (repo is null)
        {
            return Empty("Repository not found.");
        }

        var snapshotQuery = db.Snapshots.AsNoTracking().Where(s => s.RepositoryId == repositoryId);
        if (!string.IsNullOrEmpty(commitSha))
        {
            snapshotQuery = snapshotQuery.Where(s => s.CommitSha == commitSha);
        }
        var snapshot = await snapshotQuery.OrderByDescending(s => s.CreatedAt).FirstOrDefaultAsync(ct);
        if (snapshot is null)
        {
            return Empty("Repository has no analyzed snapshot yet. Run an analysis first.");
        }

        var nodes = await db.KnowledgeNodes.AsNoTracking()
            .Where(n => n.SnapshotId == snapshot.Id)
            .ToListAsync(ct);
        var edges = await db.GraphEdges.AsNoTracking()
            .Where(e => e.SnapshotId == snapshot.Id)
            .ToListAsync(ct);

        var source = await GetOverviewSourceAsync(repo, snapshot.Id, nodes, edges, ct);
        var critical = await queries.TopByDegreeAsync(repositoryId, snapshot.CommitSha, CriticalTop, ct);

        return new ExplainResult(
            HasSnapshot: true,
            EmptyReason: null,
            CommitSha: snapshot.CommitSha,
            Summary: ExtractSection(source.Markdown, "Summary"),
            Diagrams: ExtractDiagrams(source.Markdown),
            RawOverview: source.Markdown,
            Model: source.Model,
            NodeCount: source.NodeCount,
            GeneratedAt: source.GeneratedAt,
            MainComponents: ParseComponents(source.Markdown, nodes.ToDictionary(n => n.Key, StringComparer.Ordinal)),
            ArchitecturalNotes: ParseBullets(source.Markdown, "Architectural notes"),
            ExternalSystems: ParseBullets(source.Markdown, "External systems"),
            CriticalComponents: critical);
    }

    private sealed record OverviewSource(string Markdown, string Model, int NodeCount, DateTimeOffset GeneratedAt);

    private async Task<OverviewSource> GetOverviewSourceAsync(
        Repository repo,
        Guid snapshotId,
        IReadOnlyList<KnowledgeNode> nodes,
        IReadOnlyList<GraphEdge> edges,
        CancellationToken ct)
    {
        var stored = await db.ProjectOverviews.AsNoTracking()
            .FirstOrDefaultAsync(o => o.SnapshotId == snapshotId, ct);
        if (stored is not null)
        {
            return new OverviewSource(stored.Content, stored.Model, stored.NodeCount, stored.GeneratedAt);
        }
        var generated = await overviewService.GenerateAsync(repo, nodes, edges, ct);
        return new OverviewSource(generated.Overview, generated.Model, generated.NodeCount, generated.GeneratedAt);
    }

    private static IReadOnlyList<ExplainedComponent> ParseComponents(
        string markdown,
        IReadOnlyDictionary<string, KnowledgeNode> nodeByKey)
    {
        var section = ExtractSection(markdown, "Main components");
        if (section is null)
        {
            return Array.Empty<ExplainedComponent>();
        }

        var components = new List<ExplainedComponent>();
        foreach (var line in section.Split('\n'))
        {
            var match = ComponentBulletRegex.Match(line);
            if (!match.Success || !nodeByKey.TryGetValue(match.Groups[1].Value.Trim(), out var node))
            {
                continue;
            }
            components.Add(new ExplainedComponent(
                node.Key,
                node.Symbol,
                node.Path,
                node.StartLine,
                node.Kind.ToString(),
                ExtractRole(node, match.Groups[2].Value.Trim())));
        }
        return components;
    }

    private static IReadOnlyList<string> ParseBullets(string markdown, string header)
    {
        var section = ExtractSection(markdown, header);
        if (section is null)
        {
            return Array.Empty<string>();
        }

        var items = new List<string>();
        foreach (var line in section.Split('\n'))
        {
            var match = BulletRegex.Match(line);
            if (match.Success && match.Groups[1].Value.Trim().Length > 0)
            {
                items.Add(match.Groups[1].Value.Trim());
            }
        }
        return items;
    }

    private static string? ExtractSection(string markdown, string header)
    {
        var lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var inSection = false;
        var sb = new StringBuilder();
        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                var title = line[3..].Trim();
                if (string.Equals(title, header, StringComparison.OrdinalIgnoreCase))
                {
                    inSection = true;
                    continue;
                }
                if (inSection)
                {
                    break;
                }
            }
            else if (inSection)
            {
                sb.AppendLine(line);
            }
        }

        var result = sb.ToString().Trim();
        return result.Length == 0 ? null : result;
    }

    private static IReadOnlyList<string> ExtractDiagrams(string markdown)
    {
        return MermaidFenceRegex.Matches(markdown)
            .Select(m => m.Groups[1].Value.Trim())
            .Where(v => v.Length > 0)
            .ToList();
    }

    private static string ExtractRole(KnowledgeNode node, string description)
    {
        var match = RoleRegex.Match(node.Content);
        if (match.Success)
        {
            return match.Groups[1].Value.Trim();
        }
        return string.IsNullOrWhiteSpace(description) ? node.Kind.ToString() : description;
    }

    private static ExplainResult Empty(string reason) => new(
        HasSnapshot: false,
        EmptyReason: reason,
        CommitSha: null,
        Summary: null,
        Diagrams: Array.Empty<string>(),
        RawOverview: null,
        Model: "",
        NodeCount: 0,
        GeneratedAt: default,
        MainComponents: Array.Empty<ExplainedComponent>(),
        ArchitecturalNotes: Array.Empty<string>(),
        ExternalSystems: Array.Empty<string>(),
        CriticalComponents: Array.Empty<CriticalComponent>());
}
