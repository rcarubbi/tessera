using System.Text;
using Tessera.Infrastructure.Ai;
using Tessera.Infrastructure.Queries;

namespace Tessera.Cli.Reports;

public static class ReportRenderers
{
    public static string Architecture(ReportData report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Architecture");
        sb.AppendLine();
        sb.AppendLine($"Commit `{report.CommitSha}` analyzed at {report.AnalyzedAt:O}.");
        sb.AppendLine();
        sb.AppendLine("## Modules");
        sb.AppendLine();
        if (report.Nodes.Count == 0)
        {
            sb.AppendLine("No entities found.");
            return sb.ToString();
        }

        foreach (var module in report.Nodes
            .GroupBy(n => ModuleOf(n.Path), StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            sb.AppendLine($"### {module.Key}");
            sb.AppendLine();
            sb.AppendLine("| Key | Symbol | Kind | Location |");
            sb.AppendLine("|-----|--------|------|----------|");
            foreach (var node in module.OrderBy(n => n.Key, StringComparer.Ordinal))
            {
                sb.AppendLine($"| `{node.Key}` | {node.Symbol} | {node.Kind} | `{node.Path}:{node.Line}` |");
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }

    public static string Dependencies(ReportData report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Dependencies");
        sb.AppendLine();
        sb.AppendLine($"Commit `{report.CommitSha}` analyzed at {report.AnalyzedAt:O}.");
        sb.AppendLine();
        sb.AppendLine("## Top dependencies by edge count");
        sb.AppendLine();
        if (report.Edges.Count == 0)
        {
            sb.AppendLine("No edges found.");
        }
        else
        {
            sb.AppendLine("| Rank | Entity | Dependencies | Location |");
            sb.AppendLine("|------|--------|--------------|----------|");
            var rank = 1;
            foreach (var dep in report.TopDependencies)
            {
                sb.AppendLine($"| {rank++} | {dep.Symbol} (`{dep.Key}`) | {dep.Count} | `{dep.Path}:{dep.Line}` |");
            }
        }

        sb.AppendLine();
        sb.AppendLine("## Cycles");
        sb.AppendLine();
        if (report.Cycles.Count == 0)
        {
            sb.AppendLine("No dependency cycles detected.");
        }
        else
        {
            sb.AppendLine($"{report.Cycles.Count} cycle(s) detected.");
            for (var i = 0; i < report.Cycles.Count; i++)
            {
                sb.AppendLine();
                sb.AppendLine($"### Cycle {i + 1}");
                sb.AppendLine();
                sb.AppendLine(string.Join(" -> ", report.Cycles[i].Path.Select(p => $"`{p}`")));
            }
        }

        sb.AppendLine();
        sb.AppendLine("## Per-entity dependents and dependencies");
        sb.AppendLine();
        var edges = report.Edges.Select(e => new { e.From, e.To, e.Type }).ToList();
        var nodesByKey = report.Nodes.ToDictionary(n => n.Key, StringComparer.Ordinal);
        foreach (var dep in report.TopDependencies)
        {
            nodesByKey.TryGetValue(dep.Key, out var node);
            sb.AppendLine($"### {dep.Symbol} (`{dep.Key}`)");
            sb.AppendLine();
            if (node is not null)
            {
                sb.AppendLine($"Location: `{node.Path}:{node.Line}`");
                sb.AppendLine();
            }
            var deps = edges.Where(e => e.From == dep.Key).OrderBy(e => e.To, StringComparer.Ordinal).ToList();
            sb.AppendLine($"Dependencies ({deps.Count}):");
            foreach (var d in deps)
            {
                sb.AppendLine($"- `{d.To}` ({d.Type})");
            }
            var consumers = edges.Where(e => e.To == dep.Key).OrderBy(e => e.From, StringComparer.Ordinal).ToList();
            sb.AppendLine();
            sb.AppendLine($"Dependents ({consumers.Count}):");
            foreach (var c in consumers)
            {
                sb.AppendLine($"- `{c.From}` ({c.Type})");
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }

    public static string Impact(ReportData report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Impact");
        sb.AppendLine();
        sb.AppendLine($"Commit `{report.CommitSha}` analyzed at {report.AnalyzedAt:O}.");
        sb.AppendLine();
        sb.AppendLine("Transitive impact (direct/indirect) for the highest-degree entities.");
        sb.AppendLine();
        var nodesByKey = report.Nodes.ToDictionary(n => n.Key, StringComparer.Ordinal);
        foreach (var impact in report.Impact)
        {
            nodesByKey.TryGetValue(impact.Entity, out var node);
            sb.AppendLine($"## {node?.Symbol ?? impact.Entity} (`{impact.Entity}`)");
            sb.AppendLine();
            if (node is not null)
            {
                sb.AppendLine($"Location: `{node.Path}:{node.Line}`");
                sb.AppendLine();
            }
            if (impact.Items.Count == 0)
            {
                sb.AppendLine("No affected entities.");
                sb.AppendLine();
                continue;
            }
            AppendImpactTable(sb, "Direct impact", impact.Items.Where(i => i.Severity == "direct").ToList());
            AppendImpactTable(sb, "Indirect impact", impact.Items.Where(i => i.Severity == "indirect").ToList());
        }
        return sb.ToString();
    }

    private static void AppendImpactTable(StringBuilder sb, string title, IReadOnlyList<ImpactItem> items)
    {
        if (items.Count == 0)
        {
            return;
        }
        sb.AppendLine($"### {title}");
        sb.AppendLine();
        sb.AppendLine("| Key | Symbol | Location | Depth | Trace |");
        sb.AppendLine("|-----|--------|----------|-------|-------|");
        foreach (var item in items)
        {
            var trace = string.Join(" -> ", item.Trace.Select(k => $"`{k}`"));
            sb.AppendLine($"| `{item.Key}` | {item.Symbol} | `{item.Path}:{item.Line}` | {item.Depth} | {trace} |");
        }
        sb.AppendLine();
    }

    private static string ModuleOf(string path) => RuleBasedArchitect.InferContext(path);
}
