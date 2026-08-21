using Microsoft.Extensions.Options;
using Tessera.Api;
using Tessera.Infrastructure;
using Tessera.Infrastructure.Ai;
using Tessera.Infrastructure.Analysis;
using Tessera.Infrastructure.Auth;
using Tessera.Infrastructure.Chat;
using Tessera.Infrastructure.GitHub;
using Tessera.Infrastructure.Queries;
using Tessera.Infrastructure.Reviews;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTesseraInfrastructure(builder.Configuration);
builder.Services.AddHttpClient();
builder.Services.Configure<GitHubOptions>(builder.Configuration.GetSection("GitHub"));
builder.Services.Configure<GitHubOAuthOptions>(builder.Configuration.GetSection("GitHubOAuth"));
builder.Services.Configure<LocalReposOptions>(builder.Configuration.GetSection("LocalRepos"));
builder.Services.Configure<AiOptions>(builder.Configuration.GetSection("Ai"));
builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection("Auth"));
builder.Services.AddSingleton<IGitHubAppClient, GitHubAppClient>();
builder.Services.AddSingleton<IGitClient, GitClient>();
builder.Services.AddSingleton<IGitHubOAuthClient>(sp =>
{
    var options = sp.GetRequiredService<IOptions<GitHubOAuthOptions>>().Value;
    return new GitHubOAuthClient(sp.GetRequiredService<IHttpClientFactory>(), options);
});
builder.Services.AddSingleton<TokenBudgetTracker>();
builder.Services.AddSingleton<AiSettingsCache>();
builder.Services.AddSingleton<ProviderRegistry>();
builder.Services.AddSingleton<IProviderRegistry>(sp => sp.GetRequiredService<ProviderRegistry>());
builder.Services.AddScoped<AiSettingsService>();
builder.Services.AddScoped<GraphQueryService>();
builder.Services.AddScoped<IOverviewService, OverviewService>();
builder.Services.AddScoped<ExplainerService>();
builder.Services.AddScoped<ImpactAnalysisService>();
builder.Services.AddScoped<ArchitectureRuleService>();
builder.Services.AddScoped<AccessControlService>();
builder.Services.AddScoped<IRetrievalService, RetrievalService>();
builder.Services.AddScoped<IArchitectureChatService, ArchitectureChatService>();
builder.Services.AddScoped<ReviewService>();
builder.Services.AddScoped<PrReviewService>();
builder.Services.AddOpenApi();
builder.Services.AddCors(options =>
{
    options.AddPolicy("web", policy =>
    {
        var allowedOrigins = (builder.Configuration["Cors:AllowedOrigins"] ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
        }
        else
        {
            policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
        }
    });
});
builder.Services
    .AddAuthentication(TesseraAuthenticationHandler.SchemeName)
    .AddScheme<TesseraAuthenticationOptions, TesseraAuthenticationHandler>(TesseraAuthenticationHandler.SchemeName, _ => { });
builder.Services.AddAuthorization();
builder.Services.AddHostedService<TesseraInitializationService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseCors("web");
app.UseAuthentication();
app.UseAuthorization();

// Public: health checks, GitHub webhook/setup (own signature/state validation), and dashboard auth flows.
app.MapHealthEndpoints();
app.MapGitHubEndpoints();
app.MapAuthEndpoints();

var protectedApi = app.MapGroup(string.Empty).RequireAuthorization();
protectedApi.MapRepositoryEndpoints();
protectedApi.MapQueryEndpoints();
protectedApi.MapChatEndpoints();
protectedApi.MapReviewEndpoints();
protectedApi.MapSettingsEndpoints();
protectedApi.MapRuleEndpoints();

app.Run();

public partial class Program { }
