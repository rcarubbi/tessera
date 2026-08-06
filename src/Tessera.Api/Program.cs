using Microsoft.EntityFrameworkCore;
using Tessera.Api;
using Tessera.Infrastructure;
using Tessera.Infrastructure.Ai;
using Tessera.Infrastructure.Chat;
using Tessera.Infrastructure.Data;
using Tessera.Infrastructure.GitHub;
using Tessera.Infrastructure.Queries;
using Tessera.Infrastructure.Reviews;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTesseraInfrastructure(builder.Configuration);
builder.Services.AddHttpClient();
builder.Services.Configure<GitHubOptions>(builder.Configuration.GetSection("GitHub"));
builder.Services.Configure<AiOptions>(builder.Configuration.GetSection("Ai"));
builder.Services.AddHttpClient<IGitHubAppClient, GitHubAppClient>();
builder.Services.AddSingleton<TokenBudgetTracker>();
builder.Services.AddSingleton<ProviderRegistry>();
builder.Services.AddSingleton<IProviderRegistry>(sp => sp.GetRequiredService<ProviderRegistry>());
builder.Services.AddScoped<GraphQueryService>();
builder.Services.AddScoped<IRetrievalService, RetrievalService>();
builder.Services.AddScoped<IArchitectureChatService, ArchitectureChatService>();
builder.Services.AddScoped<ReviewService>();
builder.Services.AddOpenApi();
builder.Services.AddCors(options =>
{
    options.AddPolicy("web", policy =>
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<TesseraDbContext>();
    if (builder.Configuration.GetValue<bool>("MigrateOnStartup", true))
    {
        await db.Database.MigrateAsync();
    }
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseCors("web");

var dashboardApiKey = builder.Configuration["Dashboard:ApiKey"];
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value ?? "";
    if (!string.IsNullOrEmpty(dashboardApiKey)
        && path.StartsWith("/api/", StringComparison.Ordinal)
        && !path.StartsWith("/api/github/", StringComparison.OrdinalIgnoreCase))
    {
        var header = context.Request.Headers.Authorization.ToString();
        var token = header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header["Bearer ".Length..] : null;
        if (!string.Equals(token, dashboardApiKey, StringComparison.Ordinal))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Unauthorized." });
            return;
        }
    }
    await next();
});

app.MapGet("/health", async (TesseraDbContext db) =>
{
    var canConnect = await db.Database.CanConnectAsync();
    return Results.Ok(new { status = canConnect ? "ok" : "degraded" });
});

app.MapGet("/api/repositories", async (TesseraDbContext db) =>
    Results.Ok(await db.Repositories.AsNoTracking().OrderByDescending(r => r.UpdatedAt).ToListAsync()));

app.MapGet("/api/repositories/{id:guid}/snapshots", async (Guid id, TesseraDbContext db) =>
    Results.Ok(await db.Snapshots.AsNoTracking()
        .Where(s => s.RepositoryId == id)
        .OrderByDescending(s => s.CreatedAt)
        .ToListAsync()));

app.MapGitHubEndpoints();
app.MapQueryEndpoints();
app.MapChatEndpoints();
app.MapReviewEndpoints();

app.Run();

public partial class Program { }
