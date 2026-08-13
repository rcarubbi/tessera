using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Tessera.Infrastructure.Ai;
using Tessera.Infrastructure.Data;

namespace Tessera.Integration.Tests;

public sealed class ProviderRegistryTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly IServiceScope _scope;
    private readonly TesseraDbContext _db;
    private readonly AiSettingsCache _cache;
    private readonly AiSettingsService _service;
    private readonly ProviderRegistry _registry;

    public ProviderRegistryTests()
    {
        var databaseName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddDbContext<TesseraDbContext>(o =>
            o.UseInMemoryDatabase(databaseName)
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
        services.AddHttpClient();
        _provider = services.BuildServiceProvider();
        _scope = _provider.CreateScope();
        _db = _scope.ServiceProvider.GetRequiredService<TesseraDbContext>();
        _cache = new AiSettingsCache(_provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<AiSettingsCache>.Instance);
        _service = new AiSettingsService(_db, _cache);
        _registry = new ProviderRegistry(_provider.GetRequiredService<IHttpClientFactory>(), _cache, NullLogger<ProviderRegistry>.Instance);
    }

    [Fact]
    public async Task Invalid_provider_base_url_is_skipped_without_publishing_a_broken_registry()
    {
        // OpenAiCompatibleChatProvider's constructor throws UriFormatException for this BaseUrl.
        await _service.SaveAsync(new AiSettingsRequest("broken", "not a valid url", "model", "key", null, null, null));

        Assert.Null(_registry.Primary);
        Assert.Equal(0, _registry.Count);
    }

    [Fact]
    public async Task Next_settings_version_recovers_after_an_invalid_provider()
    {
        await _service.SaveAsync(new AiSettingsRequest("broken", "not a valid url", "model", "key", null, null, null));
        Assert.Null(_registry.Primary);

        await _service.SaveAsync(new AiSettingsRequest("working", "https://api.example.com", "model", "key", null, null, null));

        Assert.NotNull(_registry.Primary);
        Assert.Equal("working", _registry.Primary!.Name);
    }

    public void Dispose()
    {
        _scope.Dispose();
        _provider.Dispose();
    }
}
