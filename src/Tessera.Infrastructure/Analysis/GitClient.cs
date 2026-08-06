using System.Diagnostics;

namespace Tessera.Infrastructure.Analysis;

public interface IGitClient
{
    Task EnsureCloneAsync(string cloneUrl, string workDir, string defaultBranch, CancellationToken ct = default);
    Task<string> ResolveHeadAsync(string workDir, string branch, CancellationToken ct = default);
    Task<IReadOnlyList<string>> ListFilesAtCommitAsync(string workDir, string commitSha, CancellationToken ct = default);
    Task<string?> ReadFileAtCommitAsync(string workDir, string commitSha, string path, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetChangedFilesAsync(string workDir, string fromCommit, string toCommit, CancellationToken ct = default);
}

public sealed class GitClient : IGitClient
{
    public async Task EnsureCloneAsync(string cloneUrl, string workDir, string defaultBranch, CancellationToken ct = default)
    {
        if (Directory.Exists(Path.Combine(workDir, ".git")))
        {
            await RunAsync(workDir, new[] { "fetch", "--all", "--prune" }, ct);
            return;
        }

        Directory.CreateDirectory(workDir);
        await RunAsync(workDir, new[] { "clone", "--no-checkout", "--branch", defaultBranch, cloneUrl, "." }, ct);
    }

    public async Task<string> ResolveHeadAsync(string workDir, string branch, CancellationToken ct = default)
    {
        var output = await RunAsync(workDir, new[] { "rev-parse", $"origin/{branch}" }, ct);
        return output.Trim();
    }

    public async Task<IReadOnlyList<string>> ListFilesAtCommitAsync(string workDir, string commitSha, CancellationToken ct = default)
    {
        var output = await RunAsync(workDir, new[] { "ls-tree", "-r", "--name-only", commitSha }, ct);
        return output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();
    }

    public async Task<string?> ReadFileAtCommitAsync(string workDir, string commitSha, string path, CancellationToken ct = default)
    {
        try
        {
            return await RunAsync(workDir, new[] { "show", $"{commitSha}:{path}" }, ct);
        }
        catch (GitCommandException)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<string>> GetChangedFilesAsync(string workDir, string fromCommit, string toCommit, CancellationToken ct = default)
    {
        var output = await RunAsync(workDir, new[] { "diff", "--name-only", fromCommit, toCommit }, ct);
        return output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();
    }

    private static async Task<string> RunAsync(string workDir, string[] args, CancellationToken ct = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start git process.");

        var stdout = await process.StandardOutput.ReadToEndAsync(ct);
        var stderr = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
        {
            throw new GitCommandException($"git {string.Join(' ', args)} failed: {stderr}");
        }

        return stdout;
    }
}

public sealed class GitCommandException(string message) : Exception(message);
