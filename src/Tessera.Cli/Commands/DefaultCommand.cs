using Spectre.Console;
using Spectre.Console.Cli;

namespace Tessera.Cli.Commands;

public sealed class DefaultCommand(IAnsiConsole console) : Command<DefaultCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
    }

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellation)
    {
        console.Write(new FigletText("tessera").Color(Color.SkyBlue1));
        console.MarkupLine("[grey]Offline knowledge-graph analysis for git repositories[/]");
        console.WriteLine();

        var table = new Table()
            .Border(TableBorder.Rounded)
            .HideHeaders();
        table.AddColumn(new TableColumn("Command").LeftAligned().Width(34));
        table.AddColumn(new TableColumn("Description").LeftAligned());
        table.AddRow("analyze [[path]]", "Parse the repo at HEAD, write Markdown + JSON reports");
        table.AddRow("report [[dir]]", "Regenerate Markdown reports from report.json");
        table.AddRow("rules validate <rules.yaml> [[dir]]", "Evaluate architecture rules against the report");
        console.Write(table);

        console.WriteLine();
        console.MarkupLine("[grey]Run 'tessera --help' for options, examples, and exit codes.[/]");
        return 2;
    }
}
