using System.Diagnostics;
using System.Text;

namespace Tessera.Infrastructure.Analysis;

public interface IGitClient
{
    Task<string> EnsureCloneAsync(string cloneUrl, string workDir, CancellationToken ct = default, string? authToken = null);
    Task<string> ResolveHeadAsync(string workDir, string branch, CancellationToken ct = default);
    Task<IReadOnlyList<string>> ListFilesAtCommitAsync(string workDir, string commitSha, CancellationToken ct = default);
    Task<string?> ReadFileAtCommitAsync(string workDir, string commitSha, string path, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetChangedFilesAsync(string workDir, string fromCommit, string toCommit, CancellationToken ct = default);
}

public sealed class GitClient : IGitClient
{
    // GitHub App installation tokens authenticate as HTTP Basic with any username; sending them via a
    // header (instead of embedding in the remote URL) keeps them out of .git/config and `git remote -v`.
    public async Task<string> EnsureCloneAsync(string cloneUrl, string workDir, CancellationToken ct = default, string? authToken = null)
    {
        var authArgs = BuildAuthArgs(authToken);
        if (Directory.Exists(Path.Combine(workDir, ".git")))
        {
            // Local repositories can be re-registered against a new mount path, so
            // always re-point origin at the current URL before fetching. Without this
            // an existing clone keeps fetching from its original (possibly dead) origin.
            await RunAsync(workDir, ["remote", "set-url", "origin", cloneUrl], ct);
            await RunAsync(workDir, [.. authArgs, "fetch", "--all", "--prune"], ct);
        }
        else
        {
            Directory.CreateDirectory(workDir);
            await RunAsync(workDir, [.. authArgs, "clone", "--no-checkout", cloneUrl, "."], ct);
        }

        return await ResolveDefaultBranchAsync(workDir, ct);
    }

    private static string[] BuildAuthArgs(string? authToken)
    {
        if (string.IsNullOrEmpty(authToken))
        {
            return [];
        }
        var basicAuth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"x-access-token:{authToken}"));
        return ["-c", $"http.extraHeader=Authorization: Basic {basicAuth}"];
    }


    private static async Task<string> ResolveDefaultBranchAsync(string workDir, CancellationToken ct)
    {
        try
        {
            var symbolicRef = await RunAsync(workDir, new[] { "symbolic-ref", "refs/remotes/origin/HEAD" }, ct);
            return symbolicRef.Trim().Replace("refs/remotes/origin/", "", StringComparison.Ordinal);
        }
        catch (GitCommandException)
        {
            return "main";
        }
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

        try
        {
            var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
            var stderrTask = process.StandardError.ReadToEndAsync(ct);
            await Task.WhenAll(stdoutTask, stderrTask);
            await process.WaitForExitAsync(ct);

            if (process.ExitCode != 0)
            {
                throw new GitCommandException($"git {RedactArgs(args)} failed: {stderrTask.Result}");
            }

            return stdoutTask.Result;
        }
        catch (OperationCanceledException)
        {
            await TryKillProcessTreeAsync(process);
            throw;
        }
    }

    // Never let a credential header reach logs or exception messages.
    private static string RedactArgs(IEnumerable<string> args) =>
        string.Join(' ', args.Select(a =>
            a.StartsWith("http.extraHeader=", StringComparison.OrdinalIgnoreCase) ? "http.extraHeader=<redacted>" : a));

    private static async Task TryKillProcessTreeAsync(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
        catch
        {
            // Best-effort cleanup; the process may have already exited or refused to terminate in time.
        }
    }
}

public sealed class GitCommandException(string message) : Exception(message);
