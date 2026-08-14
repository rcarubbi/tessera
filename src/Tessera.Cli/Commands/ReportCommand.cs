using System.Text.Json;
using Spectre.Console;
using Spectre.Console.Cli;
using Tessera.Cli.Reports;
using Tessera.Cli.Settings;

namespace Tessera.Cli.Commands;

public sealed class ReportCommand(IAnsiConsole console) : AsyncCommand<ReportSettings>
{
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        ReportSettings settings,
        CancellationToken ct)
        => await RunAsync(settings.Dir, ct);

    public async Task<int> RunAsync(string reportDir, CancellationToken ct = default)
    {
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

        console.Status().Start("Regenerating Markdown reports…", _ => ReportWriter.Write(report, reportDir));
        console.MarkupLine($"[green]Regenerated reports in[/] [cyan]{Path.GetFullPath(reportDir).EscapeMarkup()}[/]");
        return 0;
    }
}
