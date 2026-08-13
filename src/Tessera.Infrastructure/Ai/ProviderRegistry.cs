using System.Net;
using Microsoft.Extensions.Logging;
using Tessera.Domain.Ports;

namespace Tessera.Infrastructure.Ai;

public interface IProviderRegistry
{
    IChatProvider? Get(string? name);
    IChatProvider? Primary { get; }
    IChatProvider? LargeTier { get; }
    IChatProvider? Fallback { get; }
    IEmbeddingProvider? Embedding { get; }
    int Count { get; }
}

public sealed class ProviderRegistry : IProviderRegistry
{
    private sealed record ProviderSnapshotCache(long Version, IReadOnlyDictionary<string, IChatProvider> Providers);

    private readonly IHttpClientFactory _factory;
    private readonly AiSettingsCache _cache;
    private readonly ILogger<ProviderRegistry> _logger;
    private ProviderSnapshotCache? _built;

    public ProviderRegistry(IHttpClientFactory factory, AiSettingsCache cache, ILogger<ProviderRegistry> logger)
    {
        _factory = factory;
        _cache = cache;
        _logger = logger;
    }

    private IReadOnlyDictionary<string, IChatProvider> Providers
    {
        get
        {
            var snapshot = _cache.GetSnapshot();
            var built = _built;
            if (built is not null && built.Version == snapshot.Version)
            {
                return built.Providers;
            }

            // Construct the full dictionary in a local variable so a partially built (or invalid) provider
            // set is never published; publish it via a single reference assignment once it's ready.
            var providers = new Dictionary<string, IChatProvider>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in snapshot.Providers)
            {
                if (string.IsNullOrEmpty(p.Name) || string.IsNullOrEmpty(p.BaseUrl))
                {
                    continue;
                }
                try
                {
                    providers[p.Name] = new OpenAiCompatibleChatProvider(_factory.CreateClient($"ai-{p.Name}"), p);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Skipping AI provider '{Provider}' due to invalid configuration.", p.Name);
                }
            }

            var next = new ProviderSnapshotCache(snapshot.Version, providers);
            _built = next;
            return providers;
        }
    }


    public IChatProvider? Get(string? name) =>
        !string.IsNullOrEmpty(name) && Providers.TryGetValue(name, out var provider) ? provider : null;

    public IChatProvider? Primary => Get(_cache.GetSnapshot().Primary) ?? Providers.Values.FirstOrDefault();

    public IChatProvider? LargeTier => Fallback ?? Primary;

    public IChatProvider? Fallback
    {
        get
        {
            var primary = Primary;
            return primary is null
                ? null
                : Providers.Values.FirstOrDefault(p =>
                    !string.Equals(p.Name, primary.Name, StringComparison.OrdinalIgnoreCase));
        }
    }

    public IEmbeddingProvider? Embedding
    {
        get
        {
            var name = _cache.GetSnapshot().Providers.FirstOrDefault(p => !string.IsNullOrEmpty(p.EmbeddingModel))?.Name;
            return name is null ? null : Get(name) as IEmbeddingProvider;
        }
    }

    public int Count => Providers.Count;
}

public static class RetryPolicy
{
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan MaxTotalDelay = TimeSpan.FromSeconds(12);

    // Use as an exception filter (`when (RetryPolicy.IsCallerCancellation(ex, ct))`) so caller
    // cancellation is re-thrown instead of being treated as a degradable provider failure.
    public static bool IsCallerCancellation(Exception ex, CancellationToken ct) =>
        ex is OperationCanceledException && ct.IsCancellationRequested;

    public static async Task<T> WithRetryAsync<T>(
        Func<CancellationToken, Task<T>> action,
        int maxRetries,
        TimeSpan? baseDelay = null,
        CancellationToken ct = default)
    {
        var delay = baseDelay ?? TimeSpan.FromSeconds(1);
        var totalWait = TimeSpan.Zero;
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await action(ct);
            }
            catch (Exception ex) when (!IsCallerCancellation(ex, ct) && attempt < maxRetries && TryGetRetryDelay(ex, delay, attempt, out var wait))
            {
                if (totalWait + wait > MaxTotalDelay)
                {
                    throw;
                }
                totalWait += wait;
                await Task.Delay(wait, ct);
            }
        }
    }

    private static bool TryGetRetryDelay(Exception ex, TimeSpan baseDelay, int attempt, out TimeSpan delay)
    {
        delay = default;
        if (ex is TimeoutException or IOException or TaskCanceledException)
        {
            delay = Cap(baseDelay * (1 << attempt));
            return true;
        }
        if (ex is ChatProviderException { StatusCode: { } status } cpe)
        {
            if (status == HttpStatusCode.TooManyRequests)
            {
                delay = cpe.RetryAfter ?? Cap(baseDelay * (1 << attempt));
                if (delay > TimeSpan.FromSeconds(5))
                {
                    // Hard quota exhaustion: retrying would only make it worse.
                    return false;
                }
                if (delay <= TimeSpan.Zero)
                {
                    delay = TimeSpan.FromSeconds(1);
                }
                return true;
            }
            if (status >= HttpStatusCode.InternalServerError)
            {
                delay = Cap(baseDelay * (1 << attempt));
                return true;
            }
            return false;
        }
        if (ex is HttpRequestException)
        {
            delay = Cap(baseDelay * (1 << attempt));
            return true;
        }
        return false;
    }

    private static TimeSpan Cap(TimeSpan value) => value > MaxRetryDelay ? MaxRetryDelay : value;
}
