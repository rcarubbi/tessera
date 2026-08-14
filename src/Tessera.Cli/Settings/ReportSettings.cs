using System.ComponentModel;
using Spectre.Console.Cli;

namespace Tessera.Cli.Settings;

public sealed class ReportSettings : CommandSettings
{
    [CommandArgument(0, "[dir]")]
    [Description("Directory containing report.json")]
    [DefaultValue("tessera-report")]
    public string Dir { get; init; } = "tessera-report";
}
