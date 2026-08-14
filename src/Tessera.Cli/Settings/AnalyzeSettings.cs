using System.ComponentModel;
using Spectre.Console.Cli;

namespace Tessera.Cli.Settings;

public sealed class AnalyzeSettings : CommandSettings
{
    [CommandArgument(0, "[path]")]
    [Description("Path to the git repository to analyze")]
    public string Path { get; init; } = ".";

    [CommandOption("-o|--output <DIR>")]
    [Description("Directory to write the reports into")]
    [DefaultValue("tessera-report")]
    public string OutputDir { get; init; } = "tessera-report";

    [CommandOption("--analyzer-url <URL>")]
    [Description("Parser sidecar base URL")]
    [DefaultValue(CliServices.DefaultAnalyzerUrl)]
    public string AnalyzerUrl { get; init; } = CliServices.DefaultAnalyzerUrl;
}
