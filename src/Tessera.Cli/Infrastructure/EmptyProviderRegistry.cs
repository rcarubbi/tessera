using Tessera.Domain.Ports;
using Tessera.Infrastructure.Ai;

namespace Tessera.Cli.Infrastructure;

// The CLI is rule-based only: linking is composed with a registry that has no AI providers, so
// ArchitectureLinkingService short-circuits to an empty edge set (matching the worker's static path).
public sealed class EmptyProviderRegistry : IProviderRegistry
{
    public IChatProvider? Get(string? name) => null;
    public IChatProvider? Primary => null;
    public IChatProvider? LargeTier => null;
    public IChatProvider? Fallback => null;
    public IEmbeddingProvider? Embedding => null;
    public int Count => 0;
}
