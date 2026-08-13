using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
    private readonly ILogger<AiSettingsCache> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private AiSettingsSnapshot? _snapshot;
    private DateTimeOffset _loadedAt = DateTimeOffset.MinValue;
    private long _version;
    private int _refreshQueued;

    public AiSettingsCache(IServiceScopeFactory scopeFactory, ILogger<AiSettingsCache> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public AiSettingsSnapshot GetSnapshot()
    {
        var snapshot = _snapshot;
        if (snapshot is not null && DateTimeOffset.UtcNow - _loadedAt <= Ttl)
        {
            return snapshot;
        }

        if (snapshot is not null && Interlocked.CompareExchange(ref _refreshQueued, 1, 0) == 0)
        {
            _ = RefreshInBackgroundAsync();
            return snapshot;
        }

        return LoadSynchronously();
    }

    // Fire-and-forget from GetSnapshot; observe any exception explicitly so it cannot surface as an
    // unhandled exception when this task is garbage collected.
    private async Task RefreshInBackgroundAsync()
    {
        try
        {
            await RefreshAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Background AI settings cache refresh failed.");
        }
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        var acquired = false;
        try
        {
            await _gate.WaitAsync(ct);
            acquired = true;
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<TesseraDbContext>();
            var settings = await db.AiSettings.AsNoTracking().OrderBy(s => s.ProviderName).ToListAsync(ct);
            _snapshot = BuildSnapshot(settings);
            _version++;
            _loadedAt = DateTimeOffset.UtcNow;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Keep previous snapshot; allow a later refresh to retry.
            _logger.LogWarning(ex, "AI settings cache refresh failed; keeping previous snapshot.");
        }
        finally
        {
            Interlocked.Exchange(ref _refreshQueued, 0);
            if (acquired)
            {
                _gate.Release();
            }
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
            var settings = db.AiSettings.AsNoTracking().OrderBy(s => s.ProviderName).ToList();
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


    private AiSettingsSnapshot BuildSnapshot(IReadOnlyList<AiSettings> rows)
    {
        var providers = new List<ProviderConfig>(rows.Count);
        foreach (var settings in rows)
        {
            if (string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                continue;
            }
            providers.Add(new ProviderConfig
            {
                Name = settings.ProviderName,
                BaseUrl = settings.BaseUrl,
                Model = settings.Model,
                ApiKey = settings.ApiKey,
                Endpoint = string.IsNullOrWhiteSpace(settings.Endpoint) ? "chat/completions" : settings.Endpoint,
                EmbeddingModel = settings.EmbeddingModel,
                EmbeddingEndpoint = string.IsNullOrWhiteSpace(settings.EmbeddingEndpoint) ? "embeddings" : settings.EmbeddingEndpoint
            });
        }

        var primary = rows.FirstOrDefault(r => r.IsPrimary && !string.IsNullOrWhiteSpace(r.BaseUrl))?.ProviderName
            ?? providers.FirstOrDefault()?.Name;
        var updatedAt = rows.Count == 0 ? null : (DateTimeOffset?)rows.Max(r => r.UpdatedAt);

        return new AiSettingsSnapshot(providers, primary, _version, updatedAt);
    }
}
