using Tessera.Domain.Merkle;
using Tessera.Domain.Parsing;

namespace Tessera.Infrastructure.Ai;

public interface ISemanticSummarizer
{
    string PromptVersion { get; }
    Task<AiContent> SummarizeAsync(
        ParsedEntity entity,
        IReadOnlyList<ParsedRelationship> relationships,
        long repositoryId,
        CancellationToken ct = default);
}

public sealed class ProviderConfig
{
    public string Name { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "";
    public string Endpoint { get; set; } = "chat/completions";
    public string? EmbeddingModel { get; set; }
    public string EmbeddingEndpoint { get; set; } = "embeddings";
}

public sealed class AiOptions
{
    public List<ProviderConfig> Providers { get; set; } = new();
    public string? Primary { get; set; }
    public string? Fallback { get; set; }
    public string? LargeTier { get; set; }
    public string? Embedding { get; set; }
    public int ComplexityThresholdLines { get; set; } = 200;
    public long DailyBudgetTokens { get; set; } = 2_000_000;
    public double ReviewThreshold { get; set; } = 0.7;
    public int MaxRetries { get; set; } = 3;
    public int TopK { get; set; } = 5;
    public double SimilarityThreshold { get; set; } = 0.5;
    public int RequestsPerMinute { get; set; }
}
