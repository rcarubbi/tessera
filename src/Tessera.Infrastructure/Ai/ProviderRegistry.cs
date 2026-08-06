using System.Net;
using Microsoft.Extensions.Options;
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
    private readonly IReadOnlyDictionary<string, IChatProvider> _providers;
    private readonly AiOptions _options;

    public ProviderRegistry(IHttpClientFactory factory, IOptions<AiOptions> options)
    {
        _options = options.Value;
        _providers = _options.Providers
            .Where(p => !string.IsNullOrEmpty(p.Name) && !string.IsNullOrEmpty(p.BaseUrl) && !string.IsNullOrEmpty(p.ApiKey))
            .ToDictionary(
                p => p.Name,
                p => (IChatProvider)new OpenAiCompatibleChatProvider(factory.CreateClient($"ai-{p.Name}"), p),
                StringComparer.OrdinalIgnoreCase);
    }

    public IChatProvider? Get(string? name) =>
        !string.IsNullOrEmpty(name) && _providers.TryGetValue(name, out var provider) ? provider : null;

    public IChatProvider? Primary => Get(_options.Primary) ?? _providers.Values.FirstOrDefault();

    public IChatProvider? LargeTier => Get(_options.LargeTier);

    public IChatProvider? Fallback => Get(_options.Fallback);

    public IEmbeddingProvider? Embedding => (Get(_options.Embedding) ?? Primary) as IEmbeddingProvider;

    public int Count => _providers.Count;
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
