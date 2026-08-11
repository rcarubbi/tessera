using Microsoft.EntityFrameworkCore;
using Tessera.Domain.Entities;
using Tessera.Infrastructure.Data;

namespace Tessera.Infrastructure.Ai;

public sealed record AiSettingsRequest(
    string ProviderName,
    string BaseUrl,
    string Model,
    string? ApiKey,
    string? Endpoint,
    string? EmbeddingModel,
    string? EmbeddingEndpoint);

public sealed record AiSettingsResponse(
    string ProviderName,
    string BaseUrl,
    string Model,
    string? ApiKeyMasked,
    bool HasApiKey,
    string? Endpoint,
    string? EmbeddingModel,
    string? EmbeddingEndpoint,
    DateTimeOffset? UpdatedAt);

public sealed class AiSettingsService(
    TesseraDbContext db,
    AiSettingsCache cache)
{
    private static string? MaskedHint(string? key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return null;
        }
        return key.Length <= 8 ? "••••••••" : $"{key[..3]}••••{key[^3..]}";
    }

    public Task<AiSettingsResponse> GetAsync(CancellationToken ct = default)
    {
        return Task.FromResult(ToResponse(cache.GetSnapshot()));
    }

    public async Task<AiSettingsResponse> SaveAsync(AiSettingsRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.ProviderName))
        {
            throw new ArgumentException("provider name is required.");
        }
        if (string.IsNullOrWhiteSpace(request.BaseUrl))
        {
            throw new ArgumentException("base url is required.");
        }
        if (string.IsNullOrWhiteSpace(request.Model))
        {
            throw new ArgumentException("model is required.");
        }

        var settings = await db.AiSettings.FirstOrDefaultAsync(ct);
        var isNew = settings is null;
        settings ??= new AiSettings { Id = Guid.NewGuid() };

        if (isNew && string.IsNullOrWhiteSpace(request.ApiKey))
        {
            throw new ArgumentException("api key is required when no key is stored yet.");
        }

        settings.ProviderName = request.ProviderName.Trim();
        settings.BaseUrl = request.BaseUrl.Trim().TrimEnd('/');
        settings.Model = request.Model.Trim();
        if (!string.IsNullOrWhiteSpace(request.Endpoint))
        {
            settings.Endpoint = request.Endpoint.Trim();
        }
        if (string.IsNullOrWhiteSpace(request.EmbeddingModel))
        {
            settings.EmbeddingModel = null;
        }
        else
        {
            settings.EmbeddingModel = request.EmbeddingModel.Trim();
        }
        if (!string.IsNullOrWhiteSpace(request.EmbeddingEndpoint))
        {
            settings.EmbeddingEndpoint = request.EmbeddingEndpoint.Trim();
        }
        if (!string.IsNullOrWhiteSpace(request.ApiKey))
        {
            settings.ApiKey = request.ApiKey.Trim();
        }
        settings.UpdatedAt = DateTimeOffset.UtcNow;

        if (isNew)
        {
            db.AiSettings.Add(settings);
        }

        await db.SaveChangesAsync(ct);
        await cache.RefreshAsync(ct);

        return ToResponse(cache.GetSnapshot());
    }

    private static AiSettingsResponse ToResponse(AiSettingsSnapshot snapshot)
    {
        var provider = snapshot.Providers.FirstOrDefault(p =>
            string.Equals(p.Name, snapshot.Primary, StringComparison.OrdinalIgnoreCase))
            ?? snapshot.Providers.FirstOrDefault();

        if (provider is null)
        {
            return new AiSettingsResponse("", "", "", null, false, null, null, null, snapshot.UpdatedAt);
        }

        return new AiSettingsResponse(
            provider.Name,
            provider.BaseUrl,
            provider.Model,
            MaskedHint(provider.ApiKey),
            !string.IsNullOrEmpty(provider.ApiKey),
            provider.Endpoint,
            provider.EmbeddingModel,
            provider.EmbeddingEndpoint,
            snapshot.UpdatedAt);
    }
}
