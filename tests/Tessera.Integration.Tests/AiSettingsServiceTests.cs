using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tessera.Domain.Entities;
using Tessera.Infrastructure.Ai;
using Tessera.Infrastructure.Data;

namespace Tessera.Integration.Tests;

public sealed class AiSettingsServiceTests
{
    private static AiOptions Config()
    {
        var options = new AiOptions
        {
            Providers = new List<ProviderConfig>
            {
                new()
                {
                    Name = "gemini",
                    BaseUrl = "https://generativelanguage.googleapis.com/v1beta/openai",
                    ApiKey = "env-gemini-key",
                    Model = "gemini-3.5-flash-lite"
                },
                new()
                {
                    Name = "qwen",
                    BaseUrl = "http://localhost:11434/v1",
                    ApiKey = "local-key",
                    Model = "qwen2.5-coder:7b"
                }
            },
            Primary = "gemini",
            Fallback = "qwen"
        };
        return options;
    }

    [Fact]
    public async Task GetAsync_returns_active_config_when_no_row()
    {
        using var ctx = CreateContext();
        var service = new AiSettingsService(ctx.Db, ctx.Cache, Options.Create(ctx.Options));

        var result = await service.GetAsync();

        Assert.Equal("gemini", result.ProviderName);
        Assert.Equal("https://generativelanguage.googleapis.com/v1beta/openai", result.BaseUrl);
        Assert.Equal("gemini-3.5-flash-lite", result.Model);
        Assert.True(result.HasApiKey);
        Assert.NotNull(result.ApiKeyMasked);
        Assert.Equal("qwen", result.FallbackProviderName);
        Assert.Null(result.UpdatedAt);
        Assert.Equal(2, result.AvailableProviders.Count);
    }

    [Fact]
    public async Task SaveAsync_persists_and_masks_api_key()
    {
        using var ctx = CreateContext();
        var service = new AiSettingsService(ctx.Db, ctx.Cache, Options.Create(ctx.Options));

        var result = await service.SaveAsync(new AiSettingsRequest(
            "gemini", "https://generativelanguage.googleapis.com/v1beta/openai", "gemini-3.5-flash-lite",
            "secret-key-1234", null));

        Assert.Equal("gemini", result.ProviderName);
        Assert.True(result.HasApiKey);
        Assert.NotNull(result.ApiKeyMasked);
        Assert.NotNull(result.UpdatedAt);
        Assert.DoesNotContain("1234", result.ApiKeyMasked);

        var row = await ctx.Db.AiSettings.AsNoTracking().SingleAsync();
        Assert.Equal("secret-key-1234", row.ApiKey);
        Assert.NotEqual(default, row.UpdatedAt);
    }

    [Fact]
    public async Task SaveAsync_requires_api_key_on_first_save()
    {
        using var ctx = CreateContext();
        var service = new AiSettingsService(ctx.Db, ctx.Cache, Options.Create(ctx.Options));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.SaveAsync(new AiSettingsRequest("gemini", "https://x", "model", null, null)));
    }

    [Fact]
    public async Task SaveAsync_keeps_existing_key_when_blank()
    {
        using var ctx = CreateContext();
        var service = new AiSettingsService(ctx.Db, ctx.Cache, Options.Create(ctx.Options));
        await service.SaveAsync(new AiSettingsRequest("gemini", "https://x", "model-a", "stored-key", null));

        var result = await service.SaveAsync(new AiSettingsRequest("gemini", "https://x", "model-b", null, null));

        Assert.True(result.HasApiKey);
        var row = await ctx.Db.AiSettings.AsNoTracking().SingleAsync();
        Assert.Equal("stored-key", row.ApiKey);
        Assert.Equal("model-b", row.Model);
    }

    [Fact]
    public async Task Cache_applies_db_override_for_existing_config_provider()
    {
        using var ctx = CreateContext();
        ctx.Db.AiSettings.Add(new AiSettings
        {
            Id = Guid.NewGuid(),
            ProviderName = "gemini",
            BaseUrl = "https://generativelanguage.googleapis.com/v1beta/openai",
            Model = "gemini-3.5-flash-lite",
            ApiKey = "db-key",
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await ctx.Db.SaveChangesAsync();
        await ctx.Cache.RefreshAsync();

        var snapshot = ctx.Cache.GetSnapshot();

        Assert.Equal("gemini", snapshot.Primary);
        var gemini = snapshot.Providers.Single(p => p.Name == "gemini");
        Assert.Equal("db-key", gemini.ApiKey);
        Assert.Equal(2, snapshot.Providers.Count);
        Assert.Equal("qwen", snapshot.Fallback);
    }

    [Fact]
    public async Task Cache_adds_custom_provider_from_db_row()
    {
        using var ctx = CreateContext();
        ctx.Db.AiSettings.Add(new AiSettings
        {
            Id = Guid.NewGuid(),
            ProviderName = "openai",
            BaseUrl = "https://api.openai.com/v1",
            Model = "gpt-4o-mini",
            ApiKey = "db-key",
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await ctx.Db.SaveChangesAsync();
        Assert.Equal(1, ctx.CountAiSettings());
        await ctx.Cache.RefreshAsync();

        var snapshot = ctx.Cache.GetSnapshot();

        Assert.Equal("openai", snapshot.Primary);
        var openai = snapshot.Providers.Single(p => p.Name == "openai");
        Assert.Equal("https://api.openai.com/v1", openai.BaseUrl);
        Assert.Equal("gpt-4o-mini", openai.Model);
        Assert.Equal("db-key", openai.ApiKey);
        Assert.Equal(3, snapshot.Providers.Count);
    }

    [Fact]
    public async Task Cache_uses_config_when_no_row()
    {
        using var ctx = CreateContext();
        await ctx.Cache.RefreshAsync();

        var snapshot = ctx.Cache.GetSnapshot();

        Assert.Equal("gemini", snapshot.Primary);
        Assert.Equal(2, snapshot.Providers.Count);
        Assert.Equal("env-gemini-key", snapshot.Providers.Single(p => p.Name == "gemini").ApiKey);
    }

    [Fact]
    public async Task Cache_override_restored_after_row_deleted()
    {
        using var ctx = CreateContext();
        ctx.Db.AiSettings.Add(new AiSettings
        {
            Id = Guid.NewGuid(),
            ProviderName = "gemini",
            BaseUrl = "https://generativelanguage.googleapis.com/v1beta/openai",
            Model = "gemini-3.5-flash-lite",
            ApiKey = "db-key",
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await ctx.Db.SaveChangesAsync();
        await ctx.Cache.RefreshAsync();
        Assert.Equal("db-key", ctx.Cache.GetSnapshot().Providers.Single(p => p.Name == "gemini").ApiKey);

        ctx.Db.AiSettings.RemoveRange(ctx.Db.AiSettings);
        await ctx.Db.SaveChangesAsync();
        await ctx.Cache.RefreshAsync();

        Assert.Equal("env-gemini-key", ctx.Cache.GetSnapshot().Providers.Single(p => p.Name == "gemini").ApiKey);
    }

    private static TestContext CreateContext()
    {
        var options = Config();
        var databaseName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddDbContext<TesseraDbContext>(o =>
            o.UseInMemoryDatabase(databaseName)
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
        var provider = services.BuildServiceProvider();
        var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TesseraDbContext>();
        var cache = new AiSettingsCache(provider.GetRequiredService<IServiceScopeFactory>(), Options.Create(options));
        return new TestContext(db, cache, options, scope, provider);
    }

    private sealed class TestContext : IDisposable
    {
        private readonly IServiceScope _scope;
        private readonly ServiceProvider _provider;

        public TestContext(TesseraDbContext db, AiSettingsCache cache, AiOptions options, IServiceScope scope, ServiceProvider provider)
        {
            Db = db;
            Cache = cache;
            Options = options;
            _scope = scope;
            _provider = provider;
        }

        public TesseraDbContext Db { get; }
        public AiSettingsCache Cache { get; }
        public AiOptions Options { get; }

        public int CountAiSettings()
        {
            using var scope = _provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<TesseraDbContext>();
            return db.AiSettings.Count();
        }

        public void Dispose()
        {
            _scope.Dispose();
            _provider.Dispose();
        }
    }
}
