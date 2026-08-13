using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Tessera.Domain.Ports;

namespace Tessera.Infrastructure.Ai;

public sealed class OpenAiCompatibleChatProvider : IChatProvider, IEmbeddingProvider, IChatStreamProvider
{
    private static readonly TimeSpan SafetyNetTimeout = TimeSpan.FromSeconds(300);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(90);

    private readonly HttpClient _http;
    private readonly ProviderConfig _config;

    public OpenAiCompatibleChatProvider(HttpClient http, ProviderConfig config)
    {
        _http = http;
        _config = config;
        _http.BaseAddress = new Uri(config.BaseUrl.TrimEnd('/') + "/");
        _http.Timeout = SafetyNetTimeout;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("tessera");
        if (!string.IsNullOrEmpty(config.ApiKey))
        {
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);
        }
    }

    public string Name => _config.Name;
    public string Model => _config.Model;
    public string EmbeddingModel => _config.EmbeddingModel ?? _config.Model;

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        var payload = new
        {
            model = EmbeddingModel,
            input = text
        };

        using var timeout = CreateTimeoutSource(ct);
        using var response = await _http.PostAsJsonAsync(_config.EmbeddingEndpoint, payload, timeout.Token);
        if (!response.IsSuccessStatusCode)
        {
            throw await BuildErrorAsync(response, timeout.Token);
        }

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
        if (doc.RootElement.TryGetProperty("data", out var data)
            && data.GetArrayLength() > 0
            && data[0].TryGetProperty("embedding", out var embedding))
        {
            var dimensions = embedding.GetArrayLength();
            var result = new float[dimensions];
            for (var i = 0; i < dimensions; i++)
            {
                result[i] = embedding[i].GetSingle();
            }
            return result;
        }

        throw new ChatProviderException($"Provider '{_config.Name}' returned an unexpected embedding response shape.");
    }

    public async Task<string> CompleteAsync(IReadOnlyList<ChatMessage> messages, CancellationToken ct = default)
    {
        var payload = new
        {
            model = _config.Model,
            messages = messages.Select(m => new { role = m.Role, content = m.Content }).ToArray(),
            temperature = 0.2,
            max_tokens = 1500
        };

        using var timeout = CreateTimeoutSource(ct);
        using var response = await _http.PostAsJsonAsync(_config.Endpoint, payload, timeout.Token);
        if (!response.IsSuccessStatusCode)
        {
            throw await BuildErrorAsync(response, timeout.Token);
        }

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
        if (doc.RootElement.TryGetProperty("choices", out var choices)
            && choices.GetArrayLength() > 0
            && choices[0].TryGetProperty("message", out var message)
            && message.TryGetProperty("content", out var content))
        {
            return content.GetString() ?? "";
        }

        throw new ChatProviderException($"Provider '{_config.Name}' returned an unexpected response shape.");
    }

    public async IAsyncEnumerable<string> StreamCompleteAsync(
        IReadOnlyList<ChatMessage> messages,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var payload = new
        {
            model = _config.Model,
            messages = messages.Select(m => new { role = m.Role, content = m.Content }).ToArray(),
            temperature = 0.2,
            max_tokens = 1500,
            stream = true
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, _config.Endpoint)
        {
            Content = JsonContent.Create(payload)
        };
        // Same request timeout as non-streaming calls applies to establishing the response; once headers
        // arrive, the read loop below is bounded only by caller cancellation since stream duration is open-ended.
        using var headersTimeout = CreateTimeoutSource(ct);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, headersTimeout.Token);
        if (!response.IsSuccessStatusCode)
        {
            throw await BuildErrorAsync(response, headersTimeout.Token);
        }

        using var body = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(body);
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(ct);
            if (line is null)
            {
                break;
            }
            if (!line.StartsWith("data:", StringComparison.Ordinal))
            {
                continue;
            }
            var data = line["data:".Length..].Trim();
            if (data == "[DONE]")
            {
                break;
            }
            if (TryParseDelta(data, out var delta) && !string.IsNullOrEmpty(delta))
            {
                yield return delta;
            }
        }
    }

    private static CancellationTokenSource CreateTimeoutSource(CancellationToken ct)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(ct);
        source.CancelAfter(RequestTimeout);
        return source;
    }

    private async Task<ChatProviderException> BuildErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var errorBody = await response.Content.ReadAsStringAsync(ct);
        TimeSpan? retryAfter = null;
        if (response.Headers.RetryAfter is { } retryAfterHeader)
        {
            retryAfter = retryAfterHeader.Delta
                ?? (retryAfterHeader.Date is { } date ? date - DateTimeOffset.UtcNow : null);
        }
        return new ChatProviderException(
            $"Provider '{_config.Name}' returned {(int)response.StatusCode}: {errorBody}",
            statusCode: response.StatusCode,
            retryAfter: retryAfter);
    }

    private static bool TryParseDelta(string data, out string? delta)
    {
        delta = null;
        try
        {
            using var doc = JsonDocument.Parse(data);
            if (doc.RootElement.TryGetProperty("choices", out var choices)
                && choices.GetArrayLength() > 0
                && choices[0].TryGetProperty("delta", out var d)
                && d.TryGetProperty("content", out var content))
            {
                delta = content.GetString();
                return true;
            }
        }
        catch (JsonException)
        {
        }
        return false;
    }
}

public sealed class ChatProviderException(string message, Exception? inner = null, HttpStatusCode? statusCode = null, TimeSpan? retryAfter = null) : Exception(message, inner)
{
    public HttpStatusCode? StatusCode { get; } = statusCode;
    public TimeSpan? RetryAfter { get; } = retryAfter;
}
