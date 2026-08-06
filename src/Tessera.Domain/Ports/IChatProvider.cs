namespace Tessera.Domain.Ports;

public sealed record ChatMessage(string Role, string Content);

public interface IChatProvider
{
    string Name { get; }
    string Model { get; }
    Task<string> CompleteAsync(IReadOnlyList<ChatMessage> messages, CancellationToken ct = default);
}
