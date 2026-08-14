using Spectre.Console;
using Spectre.Console.Testing;
using Tessera.Cli;
using Tessera.Cli.Commands;
using Tessera.Domain.Enums;
using Tessera.Domain.Parsing;
using Tessera.Infrastructure.Analysis;

namespace Tessera.Cli.Tests;

public static class CliTestFixture
{
    public static string RepoRoot => FindRepoRoot();

    public static string FixtureRepo => Path.Combine(RepoRoot, "e2e", "origin");

    public static CliServices Services(IParserSidecarClient? parser = null) => new("http://localhost:4350", parser);

    public static AnalyzeCommand AnalyzeCommand(IParserSidecarClient? parser = null, IAnsiConsole? console = null)
        => new(_ => Services(parser), console ?? new TestConsole());

    public static ReportCommand ReportCommand(IAnsiConsole? console = null) => new(console ?? new TestConsole());

    public static RulesValidateCommand RulesValidateCommand(IAnsiConsole? console = null) => new(console ?? new TestConsole());

    public static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tessera-cli-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Tessera.slnx")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new DirectoryNotFoundException("Repo root not found.");
    }
}

public sealed class FakeParser(ParseResult result) : IParserSidecarClient
{
    public Task<ParseResult> ParseAsync(string commitSha, string defaultBranch, IReadOnlyList<ParsedSourceFile> files, CancellationToken ct = default)
    {
        result.CommitSha = commitSha;
        result.DefaultBranch = defaultBranch;
        return Task.FromResult(result);
    }

    public static FakeParser WithFixture()
    {
        var result = new ParseResult
        {
            Entities =
            {
                new ParsedEntity
                {
                    Key = "src/Order.cs::Order", Path = "src/Order.cs", Symbol = "Order",
                    Kind = NodeKind.Class, Language = "csharp", StartLine = 3, EndLine = 9, StructuralHash = "h1"
                },
                new ParsedEntity
                {
                    Key = "src/OrderService.cs::OrderService", Path = "src/OrderService.cs", Symbol = "OrderService",
                    Kind = NodeKind.Class, Language = "csharp", StartLine = 3, EndLine = 14, StructuralHash = "h2"
                }
            },
            Relationships =
            {
                new ParsedRelationship
                {
                    From = "src/OrderService.cs::OrderService", To = "src/Order.cs::Order",
                    Type = EdgeType.FieldDependency, Confidence = 1.0, IsStatic = true
                }
            }
        };
        return new FakeParser(result);
    }
}

public sealed class ThrowingParser(Exception exception) : IParserSidecarClient
{
    public Task<ParseResult> ParseAsync(string commitSha, string defaultBranch, IReadOnlyList<ParsedSourceFile> files, CancellationToken ct = default)
        => throw exception;
}
