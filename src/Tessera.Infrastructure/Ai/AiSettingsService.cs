using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tessera.Domain.Entities;
using Tessera.Infrastructure.Data;

namespace Tessera.Infrastructure.Ai;

public sealed record AiSettingsRequest(
    string ProviderName,
    string BaseUrl,
    string Model,
    string? ApiKey,
    string? FallbackProviderName);

public sealed record AiProviderCatalogItem(string Name, string BaseUrl, string Model);

public sealed record AiSettingsResponse(
    string ProviderName,
    string BaseUrl,
    string Model,
    string? ApiKeyMasked,
    bool HasApiKey,
    string? FallbackProviderName,
    DateTimeOffset? UpdatedAt,
    IReadOnlyList<AiProviderCatalogItem> AvailableProviders);

public sealed class AiSettingsService(
    TesseraDbContext db,
    AiSettingsCache cache,
    IOptions<AiOptions> options)
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
        return Task.FromResult(ToResponse(cache.GetSnapshot(), Catalog()));
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
        settings.FallbackProviderName = string.IsNullOrWhiteSpace(request.FallbackProviderName)
            ? null
            : request.FallbackProviderName.Trim();
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

        return ToResponse(cache.GetSnapshot(), Catalog());
    }

    private IReadOnlyList<AiProviderCatalogItem> Catalog()
        => options.Value.Providers
            .Where(p => !string.IsNullOrEmpty(p.Name))
            .Select(p => new AiProviderCatalogItem(p.Name, p.BaseUrl, p.Model))
            .ToList();

    private static AiSettingsResponse ToResponse(AiSettingsSnapshot snapshot, IReadOnlyList<AiProviderCatalogItem> catalog)
    {
        var primary = snapshot.Primary;
        var provider = snapshot.Providers.FirstOrDefault(p =>
            string.Equals(p.Name, primary, StringComparison.OrdinalIgnoreCase))
            ?? snapshot.Providers.FirstOrDefault();

        if (provider is null)
        {
            return new AiSettingsResponse("", "", "", null, false, snapshot.Fallback, snapshot.UpdatedAt, catalog);
        }

        return new AiSettingsResponse(
            provider.Name,
            provider.BaseUrl,
            provider.Model,
            MaskedHint(provider.ApiKey),
            !string.IsNullOrEmpty(provider.ApiKey),
            snapshot.Fallback,
            snapshot.UpdatedAt,
            catalog);
    }
}
