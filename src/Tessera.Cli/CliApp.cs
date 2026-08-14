using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;
using Tessera.Cli.Commands;
using Tessera.Cli.Infrastructure;
using Tessera.Infrastructure.Analysis;

namespace Tessera.Cli;

public static class CliApp
{
    public static IServiceCollection ConfigureServices(IParserSidecarClient? parser = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<Func<string, CliServices>>(_ => url => new CliServices(url, parser));
        services.AddSingleton<AnalyzeCommand>();
        services.AddSingleton<ReportCommand>();
        services.AddSingleton<RulesValidateCommand>();
        services.AddSingleton<DefaultCommand>();
        return services;
    }

    public static CommandApp<DefaultCommand> Build(IServiceCollection services)
    {
        var app = new CommandApp<DefaultCommand>(new TypeRegistrar(services));
        app.Configure(config =>
        {
            config.SetApplicationName("tessera");
            config.SetApplicationVersion(typeof(CliApp).Assembly.GetName().Version?.ToString(3) ?? "0.1.0");

            config.AddCommand<AnalyzeCommand>("analyze")
                .WithDescription("Analyze a git repository and write Markdown + JSON reports")
                .WithExample("analyze", "/path/to/repo")
                .WithExample("analyze", ".", "--output", "reports", "--analyzer-url", "http://localhost:4350");

            config.AddCommand<ReportCommand>("report")
                .WithDescription("Regenerate Markdown reports from an existing report.json")
                .WithExample("report");

            config.AddBranch("rules", rules =>
            {
                rules.SetDescription("Evaluate architecture rules against a report");
                rules.AddCommand<RulesValidateCommand>("validate")
                    .WithDescription("Evaluate rules YAML against the report graph")
                    .WithExample("rules", "validate", "rules.yaml");
                rules.SetDefaultCommand<RulesValidateCommand>();
            });

            config.SetExceptionHandler((ex, _) =>
            {
                var code = ex switch
                {
                    CommandParseException => 2,
                    CommandRuntimeException => 2,
                    OperationCanceledException => 130,
                    _ => 3
                };
                if (code != 130)
                {
                    Console.Error.WriteLine($"error: {ex.Message}");
                }
                return code;
            });
        });
        return app;
    }

    public static async Task<int> RunAsync(
        string[] args,
        IServiceCollection? services = null,
        CancellationToken ct = default)
    {
        var app = Build(services ?? ConfigureServices());
        var code = await app.RunAsync(args, ct);
        return code == -1 ? 2 : code;
    }
}
