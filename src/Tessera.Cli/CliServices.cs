using Microsoft.Extensions.Options;
using Tessera.Cli.Git;
using Tessera.Cli.Infrastructure;
using Tessera.Infrastructure.Ai;
using Tessera.Infrastructure.Analysis;

namespace Tessera.Cli;

// Manual DI composition (design D1): only the services the CLI needs are constructed. The AI-backed
// ArchitectureLinkingService gets an empty provider registry so it contributes no cross-technology edges.
public sealed class CliServices
{
    public const string DefaultAnalyzerUrl = "http://localhost:4350";

    public CliServices(string analyzerUrl, IParserSidecarClient? parser = null)
    {
        AnalyzerUrl = analyzerUrl;
        Parser = parser ?? new ParserSidecarClient(
            new SingleHttpClientFactory(new HttpClient()),
            Options.Create(new ParserSidecarOptions { BaseUrl = analyzerUrl }));
        Summarizer = new RuleBasedSummarizer();
        Linking = new ArchitectureLinkingService(
            new EmptyProviderRegistry(),
            new TokenBudgetTracker(Options.Create(new AiOptions())),
            Options.Create(new AiOptions()));
        Git = new LocalGit();
    }

    public string AnalyzerUrl { get; }
    public IParserSidecarClient Parser { get; }
    public RuleBasedSummarizer Summarizer { get; }
    public IArchitectureLinkingService Linking { get; }
    public ILocalGit Git { get; }
}
