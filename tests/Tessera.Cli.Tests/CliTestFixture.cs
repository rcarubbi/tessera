using System.Diagnostics;
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
    // A throwaway git repo with tracked source files. The committed e2e/origin
    // fixture is gitignored (it owns its own .git) and is absent on CI, so the
    // CLI tests provision their own copy in the temp directory instead.
    private static readonly Lazy<string> _fixtureRepo = new(EnsureFixtureRepo, LazyThreadSafetyMode.ExecutionAndPublication);

    public static string FixtureRepo => _fixtureRepo.Value;

    public static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tessera-cli-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static CliServices Services(IParserSidecarClient? parser = null) => new("http://localhost:4350", parser);

    public static AnalyzeCommand AnalyzeCommand(IParserSidecarClient? parser = null, IAnsiConsole? console = null)
        => new(_ => Services(parser), console ?? new TestConsole());

    public static ReportCommand ReportCommand(IAnsiConsole? console = null) => new(console ?? new TestConsole());

    public static RulesValidateCommand RulesValidateCommand(IAnsiConsole? console = null) => new(console ?? new TestConsole());

    private static string EnsureFixtureRepo()
    {
        var root = Path.Combine(Path.GetTempPath(), "tessera-cli-tests", "fixture-origin");
        if (Directory.Exists(Path.Combine(root, ".git")))
        {
            return root;
        }

        Directory.CreateDirectory(root);
        RunGit(root, "init", "-b", "main");
        RunGit(root, "config", "user.email", "cli-tests@tessera.dev");
        RunGit(root, "config", "user.name", "CLI Tests");
        foreach (var (relative, content) in FixtureFiles)
        {
            var full = Path.Combine(root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content);
        }
        RunGit(root, "add", ".");
        RunGit(root, "commit", "-m", "initial");
        return root;
    }

    private static readonly (string Relative, string Content)[] FixtureFiles =
    [
        ("Order.cs", """
            namespace Sample;

            public class Order : IAuditable
            {
                public int Id { get; set; }
                public decimal Total { get; set; }
                public DateTime? UpdatedAt { get; set; }
            }
            """),
        ("OrderService.cs", """
            namespace Sample;

            public class OrderService : Order
            {
                public decimal ApplyDiscount(decimal rate) => Total * (1 - rate);
            }
            """),
        ("Program.cs", """
            namespace Sample;

            public static class Program
            {
                public static void Main() => _ = new OrderService();
            }
            """),
        ("Audit.cs", """
            namespace Sample;

            public class Audit
            {
                public void Log(Order order) => System.Console.WriteLine(order.Total);
            }
            """),
        ("Discount.cs", """
            namespace E2E;

            public class Discount
            {
                public double Rate { get; set; } = 0.1;

                public double Apply(Order order) => order.Total * Rate;
            }
            """),
        ("IAuditable.cs", """
            namespace Sample;

            public interface IAuditable
            {
                DateTime? UpdatedAt { get; set; }
            }
            """),
        ("Payment.cs", """
            namespace Sample;

            public class Payment
            {
                private readonly Order _order;

                public Payment(Order order)
                {
                    _order = order;
                }

                public decimal Total => _order.Total;

                public bool Process() => Total > 0;
            }
            """),
        (Path.Combine("Payments", "PaymentController.cs"), """
            namespace Sample;

            public class PaymentController
            {
                private readonly Payment _payment;

                public PaymentController(Payment payment)
                {
                    _payment = payment;
                }

                public bool Pay() => _payment.Process();
            }
            """)
    ];

    private static void RunGit(string repoRoot, params string[] args)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start git.");
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"git {string.Join(' ', args)} failed: {stderr}");
        }
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
