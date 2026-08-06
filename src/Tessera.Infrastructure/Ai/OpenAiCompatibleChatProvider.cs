using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Tessera.Domain.Ports;

namespace Tessera.Infrastructure.Ai;

public sealed class OpenAiCompatibleChatProvider : IChatProvider, IEmbeddingProvider, IChatStreamProvider
{
    private readonly HttpClient _http;
    private readonly ProviderConfig _config;

    public OpenAiCompatibleChatProvider(HttpClient http, ProviderConfig config)
    {
        _http = http;
        _config = config;
        _http.BaseAddress = new Uri(config.BaseUrl.TrimEnd('/') + "/");
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

        var response = await _http.PostAsJsonAsync(_config.EmbeddingEndpoint, payload, ct);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            throw new ChatProviderException($"Provider '{_config.Name}' returned {(int)response.StatusCode}: {errorBody}");
        }

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
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

        var response = await _http.PostAsJsonAsync(_config.Endpoint, payload, ct);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            throw new ChatProviderException($"Provider '{_config.Name}' returned {(int)response.StatusCode}: {errorBody}");
        }

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
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
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            throw new ChatProviderException($"Provider '{_config.Name}' returned {(int)response.StatusCode}: {errorBody}");
        }

        using var body = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(body);
        while (!reader.EndOfStream)
        {
            ct.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(ct);
            if (line is null || !line.StartsWith("data:", StringComparison.Ordinal))
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

public sealed class ChatProviderException(string message, Exception? inner = null) : Exception(message, inner);
