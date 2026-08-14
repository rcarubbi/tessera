using System.Diagnostics;
using Tessera.Infrastructure.Analysis;

namespace Tessera.Cli.Git;

public interface ILocalGit
{
    Task<string> ResolveHeadAsync(string workDir, CancellationToken ct = default);
    Task<string> CurrentBranchAsync(string workDir, CancellationToken ct = default);
    Task<IReadOnlyList<string>> ListTrackedFilesAsync(string workDir, CancellationToken ct = default);
    Task<string?> ReadFileAtCommitAsync(string workDir, string commitSha, string path, CancellationToken ct = default);
}

// Git operations against a local working repo at its HEAD commit. Unlike GitClient (which targets clones
// with a remote origin), this reads the working tree's own refs and index.
public sealed class LocalGit : ILocalGit
{
    public async Task<string> ResolveHeadAsync(string workDir, CancellationToken ct = default) =>
        (await RunAsync(workDir, ["rev-parse", "HEAD"], ct)).Trim();

    public async Task<string> CurrentBranchAsync(string workDir, CancellationToken ct = default) =>
        (await RunAsync(workDir, ["rev-parse", "--abbrev-ref", "HEAD"], ct)).Trim();

    public async Task<IReadOnlyList<string>> ListTrackedFilesAsync(string workDir, CancellationToken ct = default)
    {
        var output = await RunAsync(workDir, ["ls-files"], ct);
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
            return await RunAsync(workDir, ["show", $"{commitSha}:{path}"], ct);
        }
        catch (GitCommandException)
        {
            return null;
        }
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

        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        await Task.WhenAll(stdoutTask, stderrTask);
        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
        {
            throw new GitCommandException($"git {string.Join(' ', args)} failed: {stderrTask.Result}");
        }

        return stdoutTask.Result;
    }
}
