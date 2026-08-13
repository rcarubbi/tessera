using Tessera.Infrastructure.Storage;

namespace Tessera.Integration.Tests;

public sealed class FileSystemObjectStoreTests : IDisposable
{
    private readonly string _root;
    private readonly FileSystemObjectStore _store;

    public FileSystemObjectStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "tessera-object-store-tests", Guid.NewGuid().ToString());
        _store = new FileSystemObjectStore(_root);
    }

    [Fact]
    public async Task PutAsync_and_GetAsync_round_trip_nested_key()
    {
        await _store.PutAsync("snapshots/hash.json", "{\"a\":1}");

        var content = await _store.GetAsync("snapshots/hash.json");

        Assert.Equal("{\"a\":1}", content);
    }

    [Fact]
    public async Task ExistsAsync_reflects_nested_key()
    {
        await _store.PutAsync("a/b/c.txt", "content");

        Assert.True(await _store.ExistsAsync("a/b/c.txt"));
        Assert.False(await _store.ExistsAsync("a/b/missing.txt"));
    }

    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("a/../../escape.txt")]
    [InlineData("..\\escape.txt")]
    [InlineData("a\\..\\..\\escape.txt")]
    public async Task PutAsync_rejects_traversal_keys(string key)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _store.PutAsync(key, "malicious"));

        var escaped = Path.Combine(Path.GetDirectoryName(_root)!, "escape.txt");
        Assert.False(File.Exists(escaped));
    }

    [Fact]
    public async Task PutAsync_rejects_rooted_key()
    {
        var rooted = OperatingSystem.IsWindows() ? "C:/Windows/escape.txt" : "/etc/escape.txt";

        await Assert.ThrowsAsync<ArgumentException>(() => _store.PutAsync(rooted, "malicious"));
    }

    [Fact]
    public async Task GetAsync_rejects_empty_key()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _store.GetAsync(""));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
