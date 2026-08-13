namespace Tessera.Domain.Entities;

public class AiSettings
{
    public Guid Id { get; set; }
    public string ProviderName { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    public string Model { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string Endpoint { get; set; } = "chat/completions";
    public string? EmbeddingModel { get; set; }
    public string EmbeddingEndpoint { get; set; } = "embeddings";
    public bool IsPrimary { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
