using System.ComponentModel;
using Spectre.Console.Cli;

namespace Tessera.Cli.Settings;

public sealed class RulesValidateSettings : CommandSettings
{
    [CommandArgument(0, "<rules.yaml>")]
    [Description("Path to the architecture rules YAML file")]
    public string RulesFile { get; init; } = string.Empty;

    [CommandArgument(1, "[dir]")]
    [Description("Directory containing report.json")]
    [DefaultValue("tessera-report")]
    public string Dir { get; init; } = "tessera-report";
}
