namespace Tessera.Domain.Ports;

public interface IEmbeddingProvider
{
    string Name { get; }
    string EmbeddingModel { get; }
    Task<float[]> EmbedAsync(string text, CancellationToken ct = default);
}
