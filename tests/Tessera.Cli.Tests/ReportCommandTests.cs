using Tessera.Cli.Commands;

namespace Tessera.Cli.Tests;

public sealed class ReportCommandTests
{
    [Fact]
    public async Task Report_regenerates_markdown_from_existing_json()
    {
        var outputDir = CliTestFixture.TempDir();
        var analyze = CliTestFixture.AnalyzeCommand(FakeParser.WithFixture());
        Assert.Equal(0, await analyze.RunAsync(CliTestFixture.FixtureRepo, outputDir));

        var architecture = Path.Combine(outputDir, "architecture.md");
        File.Delete(architecture);
        Assert.False(File.Exists(architecture));

        var code = await CliTestFixture.ReportCommand().RunAsync(outputDir);

        Assert.Equal(0, code);
        Assert.True(File.Exists(architecture));
        Assert.Contains("## Modules", File.ReadAllText(architecture));
    }
}
