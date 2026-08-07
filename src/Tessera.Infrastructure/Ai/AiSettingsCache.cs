using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tessera.Domain.Entities;
using Tessera.Infrastructure.Data;

namespace Tessera.Infrastructure.Ai;

public sealed record AiSettingsSnapshot(
    IReadOnlyList<ProviderConfig> Providers,
    string? Primary,
    string? Fallback,
    string? LargeTier,
    string? Embedding,
    long Version,
    DateTimeOffset? UpdatedAt);

public sealed class AiSettingsCache
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AiOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private AiSettingsSnapshot? _snapshot;
    private DateTimeOffset _loadedAt = DateTimeOffset.MinValue;
    private long _version;
    private bool _refreshQueued;

    public AiSettingsCache(IServiceScopeFactory scopeFactory, IOptions<AiOptions> options)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
    }

    public AiSettingsSnapshot GetSnapshot()
    {
        var snapshot = _snapshot;
        if (snapshot is not null && DateTimeOffset.UtcNow - _loadedAt <= Ttl)
        {
            return snapshot;
        }

        if (snapshot is not null && !_refreshQueued)
        {
            _refreshQueued = true;
            _ = RefreshAsync();
            return snapshot;
        }

        return LoadSynchronously();
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        try
        {
            await _gate.WaitAsync(ct);
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<TesseraDbContext>();
            var settings = await db.AiSettings.AsNoTracking().FirstOrDefaultAsync(ct);
            _snapshot = BuildSnapshot(settings);
            _version++;
            _loadedAt = DateTimeOffset.UtcNow;
        }
        catch
        {
            // Keep previous snapshot; allow a later refresh to retry.
        }
        finally
        {
            _refreshQueued = false;
            _gate.Release();
        }
    }

    private AiSettingsSnapshot LoadSynchronously()
    {
        _gate.Wait();
        try
        {
            if (_snapshot is not null && DateTimeOffset.UtcNow - _loadedAt <= Ttl)
            {
                return _snapshot;
            }

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<TesseraDbContext>();
            var settings = db.AiSettings.AsNoTracking().FirstOrDefault();
            _snapshot = BuildSnapshot(settings);
            _version++;
            _loadedAt = DateTimeOffset.UtcNow;
            return _snapshot;
        }
        finally
        {
            _gate.Release();
        }
    }

    private AiSettingsSnapshot BuildSnapshot(AiSettings? settings)
    {
        var providers = _options.Providers
            .Select(p => new ProviderConfig
            {
                Name = p.Name,
                BaseUrl = p.BaseUrl,
                ApiKey = p.ApiKey,
                Model = p.Model,
                Endpoint = p.Endpoint,
                EmbeddingModel = p.EmbeddingModel,
                EmbeddingEndpoint = p.EmbeddingEndpoint
            })
            .ToList();

        var primary = _options.Primary;
        var fallback = _options.Fallback;

        if (settings is not null && !string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            var existing = providers.FirstOrDefault(p =>
                string.Equals(p.Name, settings.ProviderName, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                existing.BaseUrl = settings.BaseUrl;
                if (!string.IsNullOrWhiteSpace(settings.Model)) existing.Model = settings.Model;
                if (!string.IsNullOrWhiteSpace(settings.ApiKey)) existing.ApiKey = settings.ApiKey;
            }
            else
            {
                providers.Add(new ProviderConfig
                {
                    Name = string.IsNullOrWhiteSpace(settings.ProviderName) ? "custom" : settings.ProviderName,
                    BaseUrl = settings.BaseUrl,
                    Model = settings.Model,
                    ApiKey = settings.ApiKey
                });
            }

            if (!string.IsNullOrWhiteSpace(settings.ProviderName))
            {
                primary = settings.ProviderName;
            }
        }

        if (!string.IsNullOrWhiteSpace(settings?.FallbackProviderName))
        {
            fallback = settings.FallbackProviderName;
        }

        return new AiSettingsSnapshot(providers, primary, fallback, _options.LargeTier, _options.Embedding, _version, settings?.UpdatedAt);
    }
}
