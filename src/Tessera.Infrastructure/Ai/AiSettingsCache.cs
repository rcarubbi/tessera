using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tessera.Domain.Entities;
using Tessera.Infrastructure.Data;

namespace Tessera.Infrastructure.Ai;

public sealed record AiSettingsSnapshot(
    IReadOnlyList<ProviderConfig> Providers,
    string? Primary,
    long Version,
    DateTimeOffset? UpdatedAt);

public sealed class AiSettingsCache
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private AiSettingsSnapshot? _snapshot;
    private DateTimeOffset _loadedAt = DateTimeOffset.MinValue;
    private long _version;
    private bool _refreshQueued;

    public AiSettingsCache(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
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
        if (settings is null || string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            return new AiSettingsSnapshot(Array.Empty<ProviderConfig>(), null, _version, settings?.UpdatedAt);
        }

        var provider = new ProviderConfig
        {
            Name = settings.ProviderName,
            BaseUrl = settings.BaseUrl,
            Model = settings.Model,
            ApiKey = settings.ApiKey,
            Endpoint = string.IsNullOrWhiteSpace(settings.Endpoint) ? "chat/completions" : settings.Endpoint,
            EmbeddingModel = settings.EmbeddingModel,
            EmbeddingEndpoint = string.IsNullOrWhiteSpace(settings.EmbeddingEndpoint) ? "embeddings" : settings.EmbeddingEndpoint
        };

        return new AiSettingsSnapshot([provider], settings.ProviderName, _version, settings.UpdatedAt);
    }
}
