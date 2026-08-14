using System.Text.Json;
using Tessera.Cli.Reports;

namespace Tessera.Cli.Tests;

public sealed class AnalyzeCommandTests
{
    [Fact]
    public async Task Analyze_writes_reports_with_expected_sections_and_counts()
    {
        var outputDir = CliTestFixture.TempDir();
        var command = CliTestFixture.AnalyzeCommand(FakeParser.WithFixture());

        var code = await command.RunAsync(CliTestFixture.FixtureRepo, outputDir);

        Assert.Equal(0, code);
        Assert.True(File.Exists(Path.Combine(outputDir, "report.json")));
        foreach (var name in new[] { "architecture.md", "dependencies.md", "impact.md" })
        {
            Assert.True(File.Exists(Path.Combine(outputDir, name)), name);
        }

        var architecture = File.ReadAllText(Path.Combine(outputDir, "architecture.md"));
        Assert.Contains("# Architecture", architecture);
        Assert.Contains("## Modules", architecture);
        Assert.Contains("`src/Order.cs:3`", architecture);

        var dependencies = File.ReadAllText(Path.Combine(outputDir, "dependencies.md"));
        Assert.Contains("## Top dependencies by edge count", dependencies);
        Assert.Contains("## Cycles", dependencies);
        Assert.Contains("### OrderService", dependencies);
        Assert.Contains("Dependencies (1):", dependencies);

        var impact = File.ReadAllText(Path.Combine(outputDir, "impact.md"));
        Assert.Contains("# Impact", impact);
        Assert.Contains("## OrderService", impact);

        var report = JsonSerializer.Deserialize<ReportData>(
            File.ReadAllText(Path.Combine(outputDir, "report.json")), ReportJson.Options);
        Assert.NotNull(report);
        Assert.Equal(2, report!.NodeCount);
        Assert.Equal(1, report.EdgeCount);
        Assert.NotEqual("", report.CommitSha);
    }
}
