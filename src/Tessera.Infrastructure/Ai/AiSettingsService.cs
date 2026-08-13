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
    string? EmbeddingEndpoint,
    bool IsPrimary = false);

public sealed record AiSettingsResponse(
    string ProviderName,
    string BaseUrl,
    string Model,
    string? ApiKeyMasked,
    bool HasApiKey,
    string? Endpoint,
    string? EmbeddingModel,
    string? EmbeddingEndpoint,
    bool IsPrimary,
    DateTimeOffset? UpdatedAt);

public sealed record AiSettingsListResponse(IReadOnlyList<AiSettingsResponse> Providers);

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

    public Task<AiSettingsListResponse> GetAsync(CancellationToken ct = default)
        => Task.FromResult(new AiSettingsListResponse(ToResponseList(cache.GetSnapshot())));

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

        var providerName = request.ProviderName.Trim();
        var settings = await db.AiSettings.FirstOrDefaultAsync(s => s.ProviderName == providerName, ct);
        var isNew = settings is null;
        settings ??= new AiSettings { Id = Guid.NewGuid(), ProviderName = providerName };

        settings.BaseUrl = request.BaseUrl.Trim().TrimEnd('/');
        settings.Model = request.Model.Trim();
        if (!string.IsNullOrWhiteSpace(request.Endpoint))
        {
            settings.Endpoint = request.Endpoint.Trim();
        }
        settings.EmbeddingModel = string.IsNullOrWhiteSpace(request.EmbeddingModel)
            ? null
            : request.EmbeddingModel.Trim();
        if (!string.IsNullOrWhiteSpace(request.EmbeddingEndpoint))
        {
            settings.EmbeddingEndpoint = request.EmbeddingEndpoint.Trim();
        }
        if (!string.IsNullOrWhiteSpace(request.ApiKey))
        {
            settings.ApiKey = request.ApiKey.Trim();
        }

        if (request.IsPrimary || isNew && !await db.AiSettings.AnyAsync(ct))
        {
            settings.IsPrimary = true;
        }
        settings.UpdatedAt = DateTimeOffset.UtcNow;

        if (settings.IsPrimary)
        {
            var otherPrimaries = await db.AiSettings
                .Where(s => s.Id != settings.Id && s.IsPrimary)
                .ToListAsync(ct);
            foreach (var other in otherPrimaries)
            {
                other.IsPrimary = false;
            }
        }

        if (isNew)
        {
            db.AiSettings.Add(settings);
        }

        await db.SaveChangesAsync(ct);
        await cache.RefreshAsync(ct);

        var snapshot = cache.GetSnapshot();
        var saved = snapshot.Providers.FirstOrDefault(p =>
            string.Equals(p.Name, providerName, StringComparison.OrdinalIgnoreCase));
        return saved is null
            ? throw new InvalidOperationException($"Provider '{providerName}' did not appear in the settings cache after save.")
            : ToResponse(saved, settings.IsPrimary, settings.UpdatedAt);
    }

    public async Task DeleteAsync(string providerName, CancellationToken ct = default)
    {
        var settings = await db.AiSettings
            .FirstOrDefaultAsync(s => s.ProviderName == providerName, ct);
        if (settings is null)
        {
            return;
        }

        var wasPrimary = settings.IsPrimary;
        db.AiSettings.Remove(settings);

        if (wasPrimary)
        {
            var replacement = await db.AiSettings
                .OrderBy(s => s.UpdatedAt)
                .FirstOrDefaultAsync(s => s.Id != settings.Id, ct);
            if (replacement is not null)
            {
                replacement.IsPrimary = true;
            }
        }

        await db.SaveChangesAsync(ct);
        await cache.RefreshAsync(ct);
    }

    public async Task SetPrimaryAsync(string providerName, CancellationToken ct = default)
    {
        var target = await db.AiSettings
            .FirstOrDefaultAsync(s => s.ProviderName == providerName, ct)
            ?? throw new KeyNotFoundException($"No AI provider named '{providerName}' is configured.");

        var others = await db.AiSettings
            .Where(s => s.Id != target.Id && s.IsPrimary)
            .ToListAsync(ct);
        foreach (var other in others)
        {
            other.IsPrimary = false;
        }
        target.IsPrimary = true;
        target.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await cache.RefreshAsync(ct);
    }

    private static IReadOnlyList<AiSettingsResponse> ToResponseList(AiSettingsSnapshot snapshot)
    {
        var primaries = new HashSet<string>(
            snapshot.Providers.Where(p => string.Equals(p.Name, snapshot.Primary, StringComparison.OrdinalIgnoreCase))
                .Select(p => p.Name),
            StringComparer.OrdinalIgnoreCase);
        return snapshot.Providers
            .Select(p => ToResponse(p, primaries.Contains(p.Name), snapshot.UpdatedAt))
            .ToList();
    }

    private static AiSettingsResponse ToResponse(
        ProviderConfig provider,
        bool isPrimary,
        DateTimeOffset? updatedAt) =>
        new(
            provider.Name,
            provider.BaseUrl,
            provider.Model,
            MaskedHint(provider.ApiKey),
            !string.IsNullOrEmpty(provider.ApiKey),
            provider.Endpoint,
            provider.EmbeddingModel,
            provider.EmbeddingEndpoint,
            isPrimary,
            updatedAt);
}
