using Tessera.Domain.Ports;
using Tessera.Infrastructure.Ai;
using Tessera.Infrastructure.Analysis;
using Tessera.Infrastructure.GitHub;

namespace Tessera.Integration.Tests;

internal sealed class RecordingGitHubClient : IGitHubAppClient
{
    private readonly bool _failFirstPost;
    private bool _failedOnce;

    public RecordingGitHubClient(bool failFirstPost = false)
    {
        _failFirstPost = failFirstPost;
    }

    public List<long> Posts { get; } = [];
    public List<long> Deleted { get; } = [];

    public Task<string> CreateInstallationAccessTokenAsync(long installationId, CancellationToken ct = default)
        => Task.FromResult("token");

    public Task<IReadOnlyList<GitHubRepoInfo>> ListInstallationRepositoriesAsync(long installationId, string token, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<GitHubRepoInfo>>(Array.Empty<GitHubRepoInfo>());

    public Task<long> PostPrCommentAsync(long installationId, string owner, string repo, int prNumber, string body, CancellationToken ct = default)
    {
        if (_failFirstPost && !_failedOnce)
        {
            _failedOnce = true;
            throw new InvalidOperationException("post failed");
        }
        Posts.Add(prNumber);
        return Task.FromResult(100L);
    }

    public Task DeletePrCommentAsync(long installationId, string owner, string repo, long commentId, CancellationToken ct = default)
    {
        Deleted.Add(commentId);
        return Task.CompletedTask;
    }
}

internal sealed class FakeGitClient(IReadOnlyList<string> changedFiles) : IGitClient
{
    public Task<string> EnsureCloneAsync(string cloneUrl, string workDir, CancellationToken ct = default, string? authToken = null) => Task.FromResult("main");
    public Task<string> ResolveHeadAsync(string workDir, string branch, CancellationToken ct = default) => Task.FromResult("head");
    public Task<IReadOnlyList<string>> ListFilesAtCommitAsync(string workDir, string commitSha, CancellationToken ct = default)
        => Task.FromResult(changedFiles);
    public Task<string?> ReadFileAtCommitAsync(string workDir, string commitSha, string path, CancellationToken ct = default)
        => Task.FromResult<string?>(null);
    public Task<IReadOnlyList<string>> GetChangedFilesAsync(string workDir, string fromCommit, string toCommit, CancellationToken ct = default)
        => Task.FromResult(changedFiles);
}

internal sealed class FakeProviderRegistry(FakeChatProvider? primary) : IProviderRegistry
{
    private readonly FakeChatProvider? _primary = primary;
    public IChatProvider? Primary => _primary;
    public IChatProvider? LargeTier => null;
    public IChatProvider? Fallback => null;
    public IEmbeddingProvider? Embedding => null;
    public int Count => _primary is null ? 0 : 1;
    public IChatProvider? Get(string? name) => _primary?.Name == name ? _primary : null;
}

internal sealed class FakeChatProvider : IChatProvider
{
    private readonly Func<IReadOnlyList<ChatMessage>, string> _handler;
    public FakeChatProvider(string name, string model, Func<IReadOnlyList<ChatMessage>, string> handler)
    {
        Name = name;
        Model = model;
        _handler = handler;
    }

    public string Name { get; }
    public string Model { get; }
    public int Calls { get; private set; }

    public Task<string> CompleteAsync(IReadOnlyList<ChatMessage> messages, CancellationToken ct = default)
    {
        Calls++;
        return Task.FromResult(_handler(messages));
    }
}
