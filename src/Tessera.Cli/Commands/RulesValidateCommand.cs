using System.Text.Json;
using Spectre.Console;
using Spectre.Console.Cli;
using Tessera.Cli.Reports;
using Tessera.Cli.Settings;
using Tessera.Domain.Entities;
using Tessera.Domain.Enums;
using Tessera.Infrastructure.Analysis;

namespace Tessera.Cli.Commands;

public sealed class RulesValidateCommand(IAnsiConsole console) : AsyncCommand<RulesValidateSettings>
{
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        RulesValidateSettings settings,
        CancellationToken ct)
        => await RunAsync(settings.RulesFile, settings.Dir, ct);

    public async Task<int> RunAsync(string rulesFile, string reportDir, CancellationToken ct = default)
    {
        if (!File.Exists(rulesFile))
        {
            Console.Error.WriteLine($"error: rules file '{rulesFile}' does not exist.");
            return 2;
        }

        var yaml = await File.ReadAllTextAsync(rulesFile, ct);
        ArchitectureRuleSet ruleSet;
        try
        {
            ruleSet = ArchitectureRuleService.Parse(yaml);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"error: invalid rules file: {ex.Message}");
            return 2;
        }

        var jsonPath = Path.Combine(reportDir, "report.json");
        if (!File.Exists(jsonPath))
        {
            Console.Error.WriteLine($"error: no report.json found in '{Path.GetFullPath(reportDir)}'. Run 'tessera analyze' first.");
            return 2;
        }

        ReportData report;
        try
        {
            await using var stream = File.OpenRead(jsonPath);
            report = await JsonSerializer.DeserializeAsync<ReportData>(stream, ReportJson.Options, ct)
                ?? throw new InvalidDataException("report.json is empty.");
        }
        catch (JsonException ex)
        {
            Console.Error.WriteLine($"error: report.json is not valid JSON: {ex.Message}");
            return 2;
        }

        var nodes = new Dictionary<string, KnowledgeNode>(StringComparer.Ordinal);
        foreach (var n in report.Nodes)
        {
            nodes[n.Key] = new KnowledgeNode
            {
                Key = n.Key,
                Symbol = n.Symbol,
                Path = n.Path,
                StartLine = n.Line,
                EndLine = n.EndLine,
                Language = n.Language,
                Kind = Enum.TryParse<NodeKind>(n.Kind, true, out var kind) ? kind : NodeKind.Class,
                Confidence = n.Confidence
            };
        }

        var edges = report.Edges.Select(e => new GraphEdge
        {
            FromKey = e.From,
            ToKey = e.To,
            Type = Enum.TryParse<EdgeType>(e.Type, true, out var type) ? type : EdgeType.References,
            Confidence = e.Confidence
        }).ToList();

        console.MarkupLine($"[bold]Validating[/] {ruleSet.Rules.Count} rule(s) against [cyan]{report.Nodes.Count.ToString().EscapeMarkup()}[/] nodes");
        var violations = ArchitectureRuleService.Evaluate(ruleSet, nodes, edges);
        if (violations.Count == 0)
        {
            console.MarkupLine($"[green]All {ruleSet.Rules.Count} rule(s) pass.[/]");
            return 0;
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .Title($"{violations.Count} violation(s)");
        table.AddColumn(new TableColumn("Rule"));
        table.AddColumn(new TableColumn("Severity"));
        table.AddColumn(new TableColumn("Path"));
        table.AddColumn(new TableColumn("Detail"));
        foreach (var violation in violations)
        {
            var severity = violation.Severity switch
            {
                RuleSeverity.Error => "[red]error[/]",
                RuleSeverity.Warning => "[yellow]warning[/]",
                _ => "[blue]info[/]"
            };
            var detail = violation.IsMissingRequirement
                ? "required dependency is missing"
                : $"{violation.FromPath}:{violation.FromLine} -> {violation.ToPath}:{violation.ToLine} ({violation.EdgeType}, confidence {violation.Confidence:F2})";
            table.AddRow(
                violation.RuleName.EscapeMarkup(),
                severity,
                violation.FromPath.EscapeMarkup(),
                detail.EscapeMarkup());
        }
        console.Write(table);
        console.MarkupLine($"[red]{violations.Count} violation(s) found.[/]");
        return 1;
    }
}
