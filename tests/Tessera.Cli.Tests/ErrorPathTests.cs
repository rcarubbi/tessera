using Tessera.Cli.Commands;

namespace Tessera.Cli.Tests;

public sealed class ErrorPathTests
{
    [Fact]
    public async Task Analyze_on_missing_path_returns_2_and_writes_nothing()
    {
        var outputDir = Path.Combine(CliTestFixture.TempDir(), "out");
        var code = await CliTestFixture.AnalyzeCommand(FakeParser.WithFixture())
            .RunAsync(Path.Combine(CliTestFixture.TempDir(), "does-not-exist"), outputDir);

        Assert.Equal(2, code);
        Assert.False(Directory.Exists(outputDir));
    }

    [Fact]
    public async Task Analyze_on_non_git_directory_returns_2_and_writes_nothing()
    {
        var outputDir = Path.Combine(CliTestFixture.TempDir(), "out");
        var code = await CliTestFixture.AnalyzeCommand(FakeParser.WithFixture())
            .RunAsync(CliTestFixture.TempDir(), outputDir);

        Assert.Equal(2, code);
        Assert.False(Directory.Exists(outputDir));
    }

    [Fact]
    public async Task Analyze_with_unreachable_sidecar_returns_3()
    {
        var command = CliTestFixture.AnalyzeCommand(new ThrowingParser(new HttpRequestException("connection refused")));
        var code = await command.RunAsync(CliTestFixture.FixtureRepo, CliTestFixture.TempDir());

        Assert.Equal(3, code);
    }

    [Fact]
    public async Task Report_with_missing_report_json_returns_2()
    {
        var code = await CliTestFixture.ReportCommand().RunAsync(CliTestFixture.TempDir());
        Assert.Equal(2, code);
    }

    [Fact]
    public async Task Rules_with_missing_report_json_returns_2()
    {
        var rulesFile = Path.Combine(CliTestFixture.TempDir(), "rules.yaml");
        File.WriteAllText(rulesFile, "rules: []");

        var code = await CliTestFixture.RulesValidateCommand().RunAsync(rulesFile, CliTestFixture.TempDir());

        Assert.Equal(2, code);
    }

    [Fact]
    public async Task Unknown_command_returns_2()
    {
        var code = await CliApp.RunAsync(["frobnicate"]);
        Assert.Equal(2, code);
    }

    [Fact]
    public async Task Rules_without_validate_subcommand_returns_2()
    {
        var code = await CliApp.RunAsync(["rules"]);
        Assert.Equal(2, code);
    }

    [Fact]
    public async Task Bare_invocation_returns_2_with_overview()
    {
        var code = await CliApp.RunAsync([]);
        Assert.Equal(2, code);
    }

    [Fact]
    public async Task Help_returns_0()
    {
        var code = await CliApp.RunAsync(["--help"]);
        Assert.Equal(0, code);
    }
}
