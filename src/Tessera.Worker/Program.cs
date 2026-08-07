using Microsoft.Extensions.Options;
using Tessera.Infrastructure;
using Tessera.Infrastructure.Ai;
using Tessera.Infrastructure.Analysis;
using Tessera.Infrastructure.Chat;
using Tessera.Infrastructure.GitHub;
using Tessera.Worker;
using Tessera.Worker.Pipeline;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddTesseraInfrastructure(builder.Configuration);
builder.Services.AddHttpClient();
builder.Services.AddSingleton<RuleBasedSummarizer>();
builder.Services.AddSingleton<TokenBudgetTracker>();
builder.Services.AddSingleton<AiSettingsCache>();
builder.Services.AddSingleton<ProviderRegistry>();
builder.Services.AddSingleton<IProviderRegistry>(sp => sp.GetRequiredService<ProviderRegistry>());
builder.Services.AddSingleton<ISemanticSummarizer, AiSummarizer>();
builder.Services.AddSingleton<IGitClient, GitClient>();
builder.Services.AddScoped<IParserSidecarClient, ParserSidecarClient>();
builder.Services.AddScoped<IOverviewService, OverviewService>();
builder.Services.AddScoped<AnalysisPipeline>();
builder.Services.Configure<GitHubOptions>(builder.Configuration.GetSection("GitHub"));
builder.Services.AddSingleton<IGitHubAppClient>(sp =>
    new GitHubAppClient(sp.GetRequiredService<IHttpClientFactory>(), sp.GetRequiredService<IOptions<GitHubOptions>>()));
builder.Services.Configure<ParserSidecarOptions>(builder.Configuration.GetSection("Sidecar"));
builder.Services.Configure<AnalysisPipelineOptions>(builder.Configuration.GetSection("Worker"));
builder.Services.Configure<AiOptions>(builder.Configuration.GetSection("Ai"));

builder.Services.AddHostedService<JobProcessor>();

var host = builder.Build();
host.Run();
