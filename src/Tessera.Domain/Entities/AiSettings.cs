namespace Tessera.Domain.Entities;

public class AiSettings
{
    public Guid Id { get; set; }
    public string ProviderName { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    public string Model { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string? FallbackProviderName { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
