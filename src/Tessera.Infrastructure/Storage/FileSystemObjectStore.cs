using System.Text;
using Tessera.Domain.Ports;

namespace Tessera.Infrastructure.Storage;

public sealed class FileSystemObjectStore : IObjectStore
{
    private readonly string _root;

    public FileSystemObjectStore(string rootPath)
    {
        _root = Path.GetFullPath(rootPath);
        Directory.CreateDirectory(_root);
    }

    public Task<string> PutAsync(string key, string content, CancellationToken ct = default)
    {
        var path = GetPath(key);
        ct.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (!File.Exists(path))
        {
            File.WriteAllText(path, content, Encoding.UTF8);
        }
        return Task.FromResult(key);
    }

    public Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        var path = GetPath(key);
        ct.ThrowIfCancellationRequested();
        if (!File.Exists(path))
        {
            return Task.FromResult<string?>(null);
        }
        return Task.FromResult<string?>(File.ReadAllText(path, Encoding.UTF8));
    }

    public Task<bool> ExistsAsync(string key, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(File.Exists(GetPath(key)));
    }

    // Resolves a key to an absolute path guaranteed to stay inside _root, rejecting traversal and rooted keys.
    private string GetPath(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Object store key must not be empty.", nameof(key));
        }

        var normalizedKey = key.Replace('\\', '/');
        if (Path.IsPathRooted(normalizedKey))
        {
            throw new ArgumentException($"Object store key '{key}' must be a relative path.", nameof(key));
        }

        var combined = Path.GetFullPath(Path.Combine(_root, normalizedKey));
        var rootWithSeparator = _root.EndsWith(Path.DirectorySeparatorChar) ? _root : _root + Path.DirectorySeparatorChar;
        if (!combined.StartsWith(rootWithSeparator, StringComparison.Ordinal))
        {
            throw new ArgumentException($"Object store key '{key}' resolves outside the configured root.", nameof(key));
        }

        return combined;
    }
}

