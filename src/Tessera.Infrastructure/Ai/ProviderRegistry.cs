using System.Net;
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
    private readonly IHttpClientFactory _factory;
    private readonly AiSettingsCache _cache;
    private long _version = -1;
    private IReadOnlyDictionary<string, IChatProvider>? _providers;

    public ProviderRegistry(IHttpClientFactory factory, AiSettingsCache cache)
    {
        _factory = factory;
        _cache = cache;
    }

    private IReadOnlyDictionary<string, IChatProvider> Providers
    {
        get
        {
            var snapshot = _cache.GetSnapshot();
            if (_providers is null || _version != snapshot.Version)
            {
                _version = snapshot.Version;
                _providers = snapshot.Providers
                    .Where(p => !string.IsNullOrEmpty(p.Name) && !string.IsNullOrEmpty(p.BaseUrl) && !string.IsNullOrEmpty(p.ApiKey))
                    .ToDictionary(
                        p => p.Name,
                        p => (IChatProvider)new OpenAiCompatibleChatProvider(_factory.CreateClient($"ai-{p.Name}"), p),
                        StringComparer.OrdinalIgnoreCase);
            }
            return _providers;
        }
    }

    public IChatProvider? Get(string? name) =>
        !string.IsNullOrEmpty(name) && Providers.TryGetValue(name, out var provider) ? provider : null;

    public IChatProvider? Primary => Get(_cache.GetSnapshot().Primary) ?? Providers.Values.FirstOrDefault();

    public IChatProvider? LargeTier => null;

    public IChatProvider? Fallback => null;

    public IEmbeddingProvider? Embedding => Primary as IEmbeddingProvider;

    public int Count => Providers.Count;
}

public static class RetryPolicy
{
    public static async Task<T> WithRetryAsync<T>(
        Func<CancellationToken, Task<T>> action,
        int maxRetries,
        TimeSpan? baseDelay = null,
        CancellationToken ct = default)
    {
        var delay = baseDelay ?? TimeSpan.FromSeconds(1);
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await action(ct);
            }
            catch (Exception ex) when (attempt < maxRetries && IsTransient(ex))
            {
                await Task.Delay(delay * (1 << attempt) + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 250)), ct);
            }
        }
    }

    private static bool IsTransient(Exception ex)
    {
        if (ex is TimeoutException or IOException or TaskCanceledException)
        {
            return true;
        }
        if (ex is ChatProviderException { InnerException: HttpRequestException inner })
        {
            ex = inner;
        }
        if (ex is HttpRequestException { StatusCode: { } status })
        {
            return status is HttpStatusCode.TooManyRequests or >= HttpStatusCode.InternalServerError;
        }
        return ex is HttpRequestException;
    }
}
