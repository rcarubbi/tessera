using System.Text.Json;
using Spectre.Console;
using Spectre.Console.Cli;
using Tessera.Cli.Reports;
using Tessera.Cli.Settings;
using Tessera.Domain.Entities;
using Tessera.Domain.Merkle;
using Tessera.Domain.Parsing;
using Tessera.Infrastructure.Analysis;

namespace Tessera.Cli.Commands;

public sealed class AnalyzeCommand(
    Func<string, CliServices> servicesFactory,
    IAnsiConsole console) : AsyncCommand<AnalyzeSettings>
{
    private const int BatchSize = 400;

    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        AnalyzeSettings settings,
        CancellationToken ct)
        => await RunAsync(settings.Path, settings.OutputDir, settings.AnalyzerUrl, ct);

    public async Task<int> RunAsync(
        string repoPath,
        string outputDir,
        string analyzerUrl = CliServices.DefaultAnalyzerUrl,
        CancellationToken ct = default)
    {
        var workDir = Path.GetFullPath(repoPath);
        if (!Directory.Exists(workDir))
        {
            Console.Error.WriteLine($"error: path '{repoPath}' does not exist.");
            return 2;
        }

        var services = servicesFactory(analyzerUrl);

        console.MarkupLine($"[bold]Analyzing[/] [cyan]{workDir.EscapeMarkup()}[/]");

        string head;
        string branch;
        IReadOnlyList<string> trackedFiles;
        try
        {
            (head, branch, trackedFiles) = await console.Status().StartAsync(
                "Reading repository metadata (git)…",
                async ctx =>
                {
                    var h = await services.Git.ResolveHeadAsync(workDir, ct);
                    var b = await services.Git.CurrentBranchAsync(workDir, ct);
                    var f = await services.Git.ListTrackedFilesAsync(workDir, ct);
                    return (h, b, f);
                });
        }
        catch (GitCommandException)
        {
            Console.Error.WriteLine($"error: '{repoPath}' is not a git repository (git rev-parse HEAD failed).");
            return 2;
        }

        var sourceFiles = new List<ParsedSourceFile>();
        foreach (var file in trackedFiles)
        {
            ct.ThrowIfCancellationRequested();
            if (!SourceFileExtensions.HasSupportedExtension(file))
            {
                continue;
            }
            var content = await services.Git.ReadFileAtCommitAsync(workDir, head, file, ct);
            if (content is not null)
            {
                sourceFiles.Add(new ParsedSourceFile(file, content));
            }
        }

        ParseResult parse;
        try
        {
            parse = await ParseInBatchesAsync(services.Parser, head, branch, sourceFiles, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException or InvalidOperationException)
        {
            Console.Error.WriteLine($"error: analyzer sidecar unreachable at {services.AnalyzerUrl} ({ex.Message}).");
            Console.Error.WriteLine("       Start the sidecar, or override with --analyzer-url <url>.");
            return 3;
        }

        console.MarkupLine($"[green]Parsed[/] {parse.Entities.Count} entities and {parse.Relationships.Count} relationships across {sourceFiles.Count} files.");
        foreach (var diagnostic in parse.Diagnostics)
        {
            console.MarkupLine($"[yellow]warning:[/] {diagnostic.EscapeMarkup()}");
        }

        var aiContent = BuildRuleBasedContent(parse, services, ct);

        await console.Status().StartAsync("Linking entities…", async _ =>
        {
            var linked = await services.Linking.LinkAsync(parse, 0, ct);
            foreach (var edge in linked)
            {
                parse.Relationships.Add(new ParsedRelationship
                {
                    From = edge.From,
                    To = edge.To,
                    Type = edge.Type,
                    Evidence = edge.Evidence,
                    Confidence = edge.Confidence,
                    IsStatic = false
                });
            }
        });

        ComposedSnapshot composed;
        try
        {
            composed = await console.Status().StartAsync(
                "Composing snapshot…",
                async _ => SnapshotComposer.Compose(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    head,
                    parse,
                    new Dictionary<string, KnowledgeNode>(StringComparer.Ordinal),
                    aiContent));
        }
        catch (ParseResultValidationException ex)
        {
            Console.Error.WriteLine($"error: parser produced invalid output: {ex.Message}");
            return 2;
        }

        var report = ReportData.Create(head, composed);

        await console.Status().StartAsync("Writing reports…", async _ =>
        {
            Directory.CreateDirectory(outputDir);
            await File.WriteAllTextAsync(
                Path.Combine(outputDir, "report.json"),
                JsonSerializer.Serialize(report, ReportJson.Options),
                ct);
            ReportWriter.Write(report, outputDir);
        });

        var summary = new Table()
            .Border(TableBorder.Rounded)
            .HideHeaders();
        summary.AddColumn(new TableColumn("Metric").LeftAligned().Width(20));
        summary.AddColumn(new TableColumn("Value").LeftAligned());
        summary.AddRow("Commit", head[..Math.Min(12, head.Length)]);
        summary.AddRow("Branch", branch);
        summary.AddRow("Files", sourceFiles.Count.ToString());
        summary.AddRow("Entities", parse.Entities.Count.ToString());
        summary.AddRow("Relationships", parse.Relationships.Count.ToString());
        summary.AddRow("Cycles", report.Cycles.Count.ToString());
        console.Write(summary);

        console.MarkupLine($"[green]Wrote reports to[/] [cyan]{Path.GetFullPath(outputDir).EscapeMarkup()}[/]");
        console.MarkupLine("[grey]  architecture.md, dependencies.md, impact.md, report.json[/]");
        if (report.Cycles.Count > 0)
        {
            console.MarkupLine($"[yellow]Detected {report.Cycles.Count} dependency cycle(s).[/]");
        }
        return 0;
    }

    private async Task<ParseResult> ParseInBatchesAsync(
        IParserSidecarClient parser,
        string head,
        string branch,
        IReadOnlyList<ParsedSourceFile> files,
        CancellationToken ct)
    {
        if (files.Count == 0)
        {
            console.MarkupLine("[yellow]warning:[/] no supported source files found.");
            return new ParseResult { CommitSha = head, DefaultBranch = branch };
        }

        var batches = files.Chunk(BatchSize).ToList();
        return await console.Progress()
            .AutoRefresh(true)
            .AutoClear(false)
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask($"Parsing {files.Count} files via analyzer…", maxValue: batches.Count);
                var merged = new ParseResult { CommitSha = head, DefaultBranch = branch };
                foreach (var batch in batches)
                {
                    ct.ThrowIfCancellationRequested();
                    var result = await parser.ParseAsync(head, branch, batch, ct);
                    merged.Entities.AddRange(result.Entities);
                    merged.Relationships.AddRange(result.Relationships);
                    merged.Diagnostics.AddRange(result.Diagnostics);
                    task.Increment(1);
                }
                return merged;
            });
    }

    private Dictionary<string, AiContent> BuildRuleBasedContent(ParseResult parse, CliServices services, CancellationToken ct)
    {
        var aiContent = new Dictionary<string, AiContent>(StringComparer.Ordinal);
        foreach (var entity in parse.Entities)
        {
            ct.ThrowIfCancellationRequested();
            var relationships = parse.Relationships
                .Where(r => r.From == entity.Key || r.To == entity.Key)
                .ToList();
            aiContent[entity.Key] = services.Summarizer.Summarize(entity, relationships);
        }
        return aiContent;
    }
}
