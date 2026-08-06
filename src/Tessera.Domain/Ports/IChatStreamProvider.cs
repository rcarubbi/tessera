using System.Runtime.CompilerServices;

namespace Tessera.Domain.Ports;

public interface IChatStreamProvider
{
    IAsyncEnumerable<string> StreamCompleteAsync(IReadOnlyList<ChatMessage> messages, CancellationToken ct = default);
}
