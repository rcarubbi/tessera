namespace Tessera.Domain.Ports;

public interface IObjectStore
{
    Task<string> PutAsync(string key, string content, CancellationToken ct = default);
    Task<string?> GetAsync(string key, CancellationToken ct = default);
    Task<bool> ExistsAsync(string key, CancellationToken ct = default);
}
