using System.Diagnostics;
using Tessera.Infrastructure.Analysis;

namespace Tessera.Integration.Tests;

public sealed class GitClientTests : IDisposable
{
    private readonly string _repoRoot;
    private readonly IGitClient _client = new GitClient();

    public GitClientTests()
    {
        _repoRoot = Path.Combine(Path.GetTempPath(), "tessera-git-client-tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_repoRoot);
        RunGit("init", "-b", "main");
        RunGit("config", "user.email", "test@tessera.dev");
        RunGit("config", "user.name", "Test");
        File.WriteAllText(Path.Combine(_repoRoot, "a.txt"), "hello");
        RunGit("add", ".");
        RunGit("commit", "-m", "initial");
    }

    [Fact]
    public async Task ListFilesAtCommitAsync_returns_committed_files()
    {
        var head = RunGit("rev-parse", "HEAD").Trim();

        var files = await _client.ListFilesAtCommitAsync(_repoRoot, head);

        Assert.Contains("a.txt", files);
    }

    [Fact]
    public async Task ListFilesAtCommitAsync_invalid_commit_throws_with_stderr_detail()
    {
        var ex = await Assert.ThrowsAsync<GitCommandException>(
            () => _client.ListFilesAtCommitAsync(_repoRoot, "not-a-real-commit"));

        Assert.Contains("ls-tree", ex.Message);
    }

    [Fact]
    public async Task ListFilesAtCommitAsync_honors_caller_cancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _client.ListFilesAtCommitAsync(_repoRoot, "HEAD", cts.Token));
    }

    [Fact]
    public async Task ReadFileAtCommitAsync_handles_large_output_without_deadlock()
    {
        var large = string.Concat(Enumerable.Repeat("x", 5_000_000));
        File.WriteAllText(Path.Combine(_repoRoot, "large.txt"), large);
        RunGit("add", ".");
        RunGit("commit", "-m", "add large file");
        var head = RunGit("rev-parse", "HEAD").Trim();

        var content = await _client.ReadFileAtCommitAsync(_repoRoot, head, "large.txt")
            .WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(large.Length, content!.Length);
    }

    private string RunGit(params string[] args)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = _repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"git {string.Join(' ', args)} failed: {stderr}");
        }
        return stdout;
    }

    public void Dispose()
    {
        if (Directory.Exists(_repoRoot))
        {
            try
            {
                var dir = new DirectoryInfo(_repoRoot);
                foreach (var file in dir.GetFiles("*", SearchOption.AllDirectories))
                {
                    file.Attributes = FileAttributes.Normal;
                }
                Directory.Delete(_repoRoot, recursive: true);
            }
            catch
            {
                // Best-effort cleanup.
            }
        }
    }
}
