using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Tessera.Domain.Entities;
using Tessera.Infrastructure.Ai;
using Tessera.Infrastructure.Data;

namespace Tessera.Integration.Tests;

public sealed class AiSettingsServiceTests
{
    [Fact]
    public async Task GetAsync_returns_empty_when_no_row()
    {
        using var ctx = CreateContext();
        var service = new AiSettingsService(ctx.Db, ctx.Cache);

        var result = await service.GetAsync();

        Assert.Equal("", result.ProviderName);
        Assert.Equal("", result.BaseUrl);
        Assert.False(result.HasApiKey);
        Assert.Null(result.ApiKeyMasked);
        Assert.Null(result.UpdatedAt);
    }

    [Fact]
    public async Task SaveAsync_persists_and_masks_api_key()
    {
        using var ctx = CreateContext();
        var service = new AiSettingsService(ctx.Db, ctx.Cache);

        var result = await service.SaveAsync(new AiSettingsRequest(
            "gemini", "https://generativelanguage.googleapis.com/v1beta/openai", "gemini-3.5-flash-lite",
            "secret-key-1234", null, null, null));

        Assert.Equal("gemini", result.ProviderName);
        Assert.Equal("gemini-3.5-flash-lite", result.Model);
        Assert.True(result.HasApiKey);
        Assert.NotNull(result.ApiKeyMasked);
        Assert.NotNull(result.UpdatedAt);
        Assert.DoesNotContain("1234", result.ApiKeyMasked);

        var row = await ctx.Db.AiSettings.AsNoTracking().SingleAsync();
        Assert.Equal("secret-key-1234", row.ApiKey);
        Assert.Equal("chat/completions", row.Endpoint);
        Assert.Equal("embeddings", row.EmbeddingEndpoint);
        Assert.Null(row.EmbeddingModel);
        Assert.NotEqual(default, row.UpdatedAt);
    }

    [Fact]
    public async Task SaveAsync_persists_embedding_fields()
    {
        using var ctx = CreateContext();
        var service = new AiSettingsService(ctx.Db, ctx.Cache);

        var result = await service.SaveAsync(new AiSettingsRequest(
            "gemini", "https://x", "model", "key", "chat/completions", "text-embedding", "embeddings"));

        Assert.Equal("text-embedding", result.EmbeddingModel);
        Assert.Equal("chat/completions", result.Endpoint);
        Assert.Equal("embeddings", result.EmbeddingEndpoint);
    }

    [Fact]
    public async Task SaveAsync_requires_api_key_on_first_save()
    {
        using var ctx = CreateContext();
        var service = new AiSettingsService(ctx.Db, ctx.Cache);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.SaveAsync(new AiSettingsRequest("gemini", "https://x", "model", null, null, null, null)));
    }

    [Fact]
    public async Task SaveAsync_keeps_existing_key_when_blank()
    {
        using var ctx = CreateContext();
        var service = new AiSettingsService(ctx.Db, ctx.Cache);
        await service.SaveAsync(new AiSettingsRequest("gemini", "https://x", "model-a", "stored-key", null, null, null));

        var result = await service.SaveAsync(new AiSettingsRequest("gemini", "https://x", "model-b", null, null, null, null));

        Assert.True(result.HasApiKey);
        var row = await ctx.Db.AiSettings.AsNoTracking().SingleAsync();
        Assert.Equal("stored-key", row.ApiKey);
        Assert.Equal("model-b", row.Model);
    }

    [Fact]
    public async Task Cache_builds_snapshot_from_saved_row()
    {
        using var ctx = CreateContext();
        var service = new AiSettingsService(ctx.Db, ctx.Cache);
        await service.SaveAsync(new AiSettingsRequest(
            "gemini", "https://generativelanguage.googleapis.com/v1beta/openai", "gemini-3.5-flash-lite",
            "db-key", null, null, null));

        var snapshot = ctx.Cache.GetSnapshot();

        Assert.Equal("gemini", snapshot.Primary);
        var provider = Assert.Single(snapshot.Providers);
        Assert.Equal("db-key", provider.ApiKey);
        Assert.Equal("gemini-3.5-flash-lite", provider.Model);
    }

    [Fact]
    public async Task Cache_is_empty_after_row_deleted()
    {
        using var ctx = CreateContext();
        var service = new AiSettingsService(ctx.Db, ctx.Cache);
        await service.SaveAsync(new AiSettingsRequest("gemini", "https://x", "model", "db-key", null, null, null));
        Assert.Single(ctx.Cache.GetSnapshot().Providers);

        ctx.Db.AiSettings.RemoveRange(ctx.Db.AiSettings);
        await ctx.Db.SaveChangesAsync();
        await ctx.Cache.RefreshAsync();

        var snapshot = ctx.Cache.GetSnapshot();
        Assert.Empty(snapshot.Providers);
        Assert.Null(snapshot.Primary);
    }

    [Fact]
    public async Task Cache_returns_empty_when_row_has_no_base_url()
    {
        using var ctx = CreateContext();
        ctx.Db.AiSettings.Add(new AiSettings
        {
            Id = Guid.NewGuid(),
            ProviderName = "gemini",
            BaseUrl = "",
            Model = "model",
            ApiKey = "key",
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await ctx.Db.SaveChangesAsync();
        await ctx.Cache.RefreshAsync();

        var snapshot = ctx.Cache.GetSnapshot();

        Assert.Empty(snapshot.Providers);
        Assert.Null(snapshot.Primary);
    }

    private static TestContext CreateContext()
    {
        var databaseName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddDbContext<TesseraDbContext>(o =>
            o.UseInMemoryDatabase(databaseName)
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
        var provider = services.BuildServiceProvider();
        var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TesseraDbContext>();
        var cache = new AiSettingsCache(provider.GetRequiredService<IServiceScopeFactory>());
        return new TestContext(db, cache, scope, provider);
    }

    private sealed class TestContext : IDisposable
    {
        private readonly IServiceScope _scope;
        private readonly ServiceProvider _provider;

        public TestContext(TesseraDbContext db, AiSettingsCache cache, IServiceScope scope, ServiceProvider provider)
        {
            Db = db;
            Cache = cache;
            _scope = scope;
            _provider = provider;
        }

        public TesseraDbContext Db { get; }
        public AiSettingsCache Cache { get; }

        public void Dispose()
        {
            _scope.Dispose();
            _provider.Dispose();
        }
    }
}
