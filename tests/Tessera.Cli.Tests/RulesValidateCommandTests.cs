using Tessera.Cli.Commands;

namespace Tessera.Cli.Tests;

public sealed class RulesValidateCommandTests
{
    private const string PassYaml = """
        rules:
          - name: no-web-to-core
            severity: warning
            deny:
              from:
                path: Web/
              to:
                path: Core/
        """;

    private const string FailYaml = """
        rules:
          - name: no-service-to-order
            severity: error
            deny:
              from:
                path: src/OrderService.cs
              to:
                path: src/Order.cs
        """;

    [Fact]
    public async Task Rules_pass_exits_zero()
    {
        var outputDir = await AnalyzeAsync();
        var rulesFile = Path.Combine(outputDir, "rules-pass.yaml");
        File.WriteAllText(rulesFile, PassYaml);

        var code = await CliTestFixture.RulesValidateCommand().RunAsync(rulesFile, outputDir);

        Assert.Equal(0, code);
    }

    [Fact]
    public async Task Rules_fail_exits_one_with_violations()
    {
        var outputDir = await AnalyzeAsync();
        var rulesFile = Path.Combine(outputDir, "rules-fail.yaml");
        File.WriteAllText(rulesFile, FailYaml);

        var code = await CliTestFixture.RulesValidateCommand().RunAsync(rulesFile, outputDir);

        Assert.Equal(1, code);
    }

    [Fact]
    public async Task Rules_invalid_yaml_exits_two()
    {
        var outputDir = await AnalyzeAsync();
        var rulesFile = Path.Combine(outputDir, "rules-invalid.yaml");
        File.WriteAllText(rulesFile, "rules:\n  - name: broken\n    deny:\n      from:\n      to: [unclosed\n");

        var code = await CliTestFixture.RulesValidateCommand().RunAsync(rulesFile, outputDir);

        Assert.Equal(2, code);
    }

    [Fact]
    public async Task Rules_missing_rules_file_exits_two()
    {
        var code = await CliTestFixture.RulesValidateCommand()
            .RunAsync(Path.Combine(CliTestFixture.TempDir(), "nope.yaml"), CliTestFixture.TempDir());
        Assert.Equal(2, code);
    }

    private static async Task<string> AnalyzeAsync()
    {
        var outputDir = CliTestFixture.TempDir();
        var code = await CliTestFixture.AnalyzeCommand(FakeParser.WithFixture()).RunAsync(CliTestFixture.FixtureRepo, outputDir);
        Assert.Equal(0, code);
        return outputDir;
    }
}
