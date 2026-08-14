using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tessera.Domain.Entities;
using Tessera.Domain.Enums;
using Tessera.Domain.Parsing;
using Tessera.Infrastructure.Ai;
using Tessera.Infrastructure.Analysis;
using Tessera.Infrastructure.Chat;
using Tessera.Infrastructure.Data;
using Tessera.Infrastructure.GitHub;
using Tessera.Infrastructure.Queries;
using Tessera.Infrastructure.Storage;
using Tessera.Worker.Pipeline;

namespace Tessera.Integration.Tests;

public sealed class EndToEndPipelineTests : IDisposable
{
    private readonly string _workRoot = Path.Combine(Path.GetTempPath(), "tessera-e2e", Guid.NewGuid().ToString("N"));
    private readonly string _objectRoot;
    private readonly string _gitRepoRoot;
    private readonly string _sidecarRoot;
    private readonly int _sidecarPort;
    private Process? _sidecarProcess;
    private readonly StringBuilder _sidecarLog = new();

    public EndToEndPipelineTests()
    {
        _objectRoot = Path.Combine(_workRoot, "objects");
        _gitRepoRoot = Path.Combine(_workRoot, "origin");
        _sidecarRoot = Path.Combine(FindRepoRoot(), "analyzers");
        _sidecarPort = GetFreePort();

        Directory.CreateDirectory(_gitRepoRoot);
        InitGitRepo(_gitRepoRoot);

        _sidecarProcess = StartSidecar(_sidecarPort, _sidecarRoot);
        _sidecarProcess.OutputDataReceived += (_, e) => { if (e.Data is not null) _sidecarLog.AppendLine(e.Data); };
        _sidecarProcess.ErrorDataReceived += (_, e) => { if (e.Data is not null) _sidecarLog.AppendLine(e.Data); };
        _sidecarProcess.BeginOutputReadLine();
        _sidecarProcess.BeginErrorReadLine();
        WaitForSidecar(_sidecarPort);
    }

    [Fact]
    public async Task Pipeline_clones_parses_and_snapshots_then_increments()
    {
        using var db = CreateDb();

        var repo = new Repository
        {
            Id = Guid.NewGuid(),
            FullName = "e2e/sample",
            CloneUrl = _gitRepoRoot,
            DefaultBranch = "main",
            Status = ProcessingStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.Repositories.Add(repo);
        await db.SaveChangesAsync();

        var pipeline = CreatePipeline(db);
        await pipeline.ProcessAsync(repo);

        Assert.Equal(ProcessingStatus.Completed, repo.Status);
        Assert.False(string.IsNullOrEmpty(repo.LastProcessedCommit));
        Assert.True(repo.NodeCount > 0);

        var snapshot = await db.Snapshots.SingleAsync(s => s.RepositoryId == repo.Id);
        Assert.Equal(repo.LastProcessedCommit, snapshot.CommitSha);
        Assert.False(string.IsNullOrEmpty(snapshot.RootHash));

        var nodes = await db.KnowledgeNodes.Where(n => n.SnapshotId == snapshot.Id).ToListAsync();
        Assert.Equal(repo.NodeCount, nodes.Count);
        Assert.Contains(nodes, n => n.Kind == NodeKind.Class);

        var provenances = await db.KnowledgeNodeProvenances.ToListAsync();
        Assert.NotEmpty(provenances);
        Assert.All(provenances, p => Assert.Equal(RuleBasedSummarizer.PromptVersionConst, p.PromptVersion));

        var orderServiceNode = Assert.Single(nodes, n => n.Key == "OrderService.cs::OrderService");
        Assert.Contains("## Architecture", orderServiceNode.Content);
        Assert.Contains("- Bounded context: Root", orderServiceNode.Content);
        Assert.Contains("- Role: Service", orderServiceNode.Content);

        var objectStorePath = Path.Combine(_objectRoot, "snapshots", $"{snapshot.RootHash}.json");
        Assert.True(File.Exists(objectStorePath), "snapshot JSON not persisted in object store");
        var stored = JsonSerializer.Deserialize<StoredSnapshot>(await File.ReadAllTextAsync(objectStorePath));
        Assert.NotNull(stored);
        Assert.Equal(snapshot.NodeCount, stored.Nodes.Count);

        CommitSecondVersion(_gitRepoRoot);

        await pipeline.ProcessAsync(repo);

        var snapshots = await db.Snapshots.Where(s => s.RepositoryId == repo.Id).ToListAsync();
        Assert.Equal(2, snapshots.Count);
        Assert.NotEqual(snapshots[0].RootHash, snapshots[1].RootHash);

        var latestNodes = await db.KnowledgeNodes.Where(n => n.SnapshotId == snapshots[1].Id).ToListAsync();
        Assert.Equal(repo.NodeCount, latestNodes.Count);
    }

    [Fact]
    public async Task Local_offline_repository_runs_to_completion_after_activation()
    {
        using var db = CreateDb();

        var repo = new Repository
        {
            Id = Guid.NewGuid(),
            GitHubId = 0,
            Owner = "local",
            Name = "offline-app",
            FullName = "offline-app",
            CloneUrl = _gitRepoRoot,
            DefaultBranch = "main",
            InstallationId = 0,
            CreatedBy = "admin",
            IsConnected = false,
            Status = ProcessingStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.Repositories.Add(repo);
        await db.SaveChangesAsync();

        repo.IsConnected = true;
        repo.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        var pipeline = CreatePipeline(db);
        await pipeline.ProcessAsync(repo);

        Assert.Equal(ProcessingStatus.Completed, repo.Status);
        Assert.False(string.IsNullOrEmpty(repo.LastProcessedCommit));
        Assert.True(repo.NodeCount > 0);
        var snapshot = await db.Snapshots.SingleAsync(s => s.RepositoryId == repo.Id);
        Assert.Equal(repo.LastProcessedCommit, snapshot.CommitSha);
    }

    [Fact]
    public async Task Pipeline_output_answers_impact_and_diff_queries()
    {
        using var db = CreateDb();

        var repo = new Repository
        {
            Id = Guid.NewGuid(),
            FullName = "e2e/query",
            CloneUrl = _gitRepoRoot,
            DefaultBranch = "main",
            Status = ProcessingStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.Repositories.Add(repo);
        await db.SaveChangesAsync();

        var pipeline = CreatePipeline(db);
        await pipeline.ProcessAsync(repo);
        var firstCommit = repo.LastProcessedCommit;

        CommitSecondVersion(_gitRepoRoot);
        await pipeline.ProcessAsync(repo);
        var secondCommit = repo.LastProcessedCommit;

        Assert.NotEqual(firstCommit, secondCommit);
        Assert.NotNull(secondCommit);

        var queries = new GraphQueryService(db);

        var impact = await queries.ImpactAsync(repo.Id, "Order.cs::Order", secondCommit);
        Assert.Equal("Order.cs::Order", impact.Entity);
        var orderDependent = Assert.Single(impact.Items, i => i.Key == "OrderService.cs::OrderService");
        Assert.Equal("direct", orderDependent.Severity);
        Assert.Equal(1, orderDependent.Depth);
        Assert.Contains("Order.cs::Order", orderDependent.Trace);
        var paymentDependent = Assert.Single(impact.Items, i => i.Key == "Payment.cs::Payment");
        Assert.Equal("direct", paymentDependent.Severity);
        Assert.Equal(1, paymentDependent.Depth);

        var diff = await queries.DiffAsync(repo.Id, firstCommit!, secondCommit);
        Assert.Contains(diff.Nodes, n => n.Change == "added" && n.Key == "Payment.cs::Payment");
        Assert.Contains(diff.Nodes, n => n.Change == "changed" && n.Key == "Order.cs::Order");
        Assert.Contains(diff.Nodes, n => n.Change == "changed" && n.Key == "OrderService.cs::OrderService");
        Assert.DoesNotContain(diff.Nodes, n => n.Key == "Program.cs::Program");
        Assert.Contains(diff.Edges, e => e.Change == "added" && e.Type == "Implements"
            && e.From == "Order.cs::Order" && e.To == "Auditable.cs::IAuditable");
        Assert.Contains(diff.Edges, e => e.Change == "added" && e.Type == "FieldDependency"
            && e.From == "Payment.cs::Payment" && e.To == "Order.cs::Order");
        Assert.Contains(diff.Edges, e => e.Change == "added" && e.Type == "Injected"
            && e.From == "Payment.cs::Payment" && e.To == "Order.cs::Order");
    }

    [Fact]
    public async Task Pipeline_marks_cancelled_when_cancel_requested_before_start()
    {
        using var db = CreateDb();

        var repo = new Repository
        {
            Id = Guid.NewGuid(),
            FullName = "e2e/cancel-early",
            CloneUrl = _gitRepoRoot,
            DefaultBranch = "main",
            Status = ProcessingStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.Repositories.Add(repo);
        await db.SaveChangesAsync();

        repo.CancelRequested = true;
        await db.SaveChangesAsync();

        var pipeline = CreatePipeline(db);
        await pipeline.ProcessAsync(repo);

        Assert.Equal(ProcessingStatus.Cancelled, repo.Status);
        Assert.False(repo.CancelRequested);
    }

    [Fact]
    public async Task Pipeline_cancels_mid_processing_when_cancel_requested()
    {
        CommitExtraFiles(_gitRepoRoot);

        using var db = CreateDb();

        var repo = new Repository
        {
            Id = Guid.NewGuid(),
            FullName = "e2e/cancel-mid",
            CloneUrl = _gitRepoRoot,
            DefaultBranch = "main",
            Status = ProcessingStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.Repositories.Add(repo);
        await db.SaveChangesAsync();

        var pipeline = CreatePipeline(db);
        var process = pipeline.ProcessAsync(repo);

        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        var cancelSet = false;
        while (DateTimeOffset.UtcNow < deadline && !process.IsCompleted)
        {
            var status = await db.Repositories.AsNoTracking()
                .Where(r => r.Id == repo.Id)
                .Select(r => r.Status)
                .FirstAsync();
            if (status is not (ProcessingStatus.Pending or ProcessingStatus.Completed or ProcessingStatus.Failed or ProcessingStatus.Cancelled))
            {
                repo.CancelRequested = true;
                await db.SaveChangesAsync();
                cancelSet = true;
                break;
            }
            await Task.Delay(25);
        }

        await process;

        Assert.True(cancelSet, "Cancel request was never applied while processing.");
        Assert.Equal(ProcessingStatus.Cancelled, repo.Status);
        Assert.False(repo.CancelRequested);
    }

    [Fact]
    public async Task Host_shutdown_cancellation_does_not_mark_the_repository_failed()
    {
        using var db = CreateDb();

        var repo = new Repository
        {
            Id = Guid.NewGuid(),
            FullName = "e2e/host-shutdown",
            CloneUrl = _gitRepoRoot,
            DefaultBranch = "main",
            Status = ProcessingStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.Repositories.Add(repo);
        await db.SaveChangesAsync();

        var pipeline = CreatePipeline(db);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // A pre-canceled token simulates host shutdown: EnsureNotCancelledAsync's own DB query throws a plain
        // OperationCanceledException (not CancelRequestedException), which must not be treated as a failure.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pipeline.ProcessAsync(repo, cts.Token));

        Assert.Equal(ProcessingStatus.Pending, repo.Status);
        Assert.Null(repo.ErrorMessage);
    }

    private static void CommitExtraFiles(string repoRoot)
    {
        for (var i = 0; i < 30; i++)
        {
            File.WriteAllText(Path.Combine(repoRoot, $"Extra{i}.cs"), $$"""
                namespace Sample;

                public class Extra{{i}}
                {
                    public int Value => {{i}};
                }
                """);
        }
        RunGit(repoRoot, "add", ".");
        RunGit(repoRoot, "commit", "-m", "add extras");
    }

    private AnalysisPipeline CreatePipeline(TesseraDbContext db)
    {
        return new AnalysisPipeline(
            db,
            new GitClient(),
            new ParserSidecarClient(new FakeHttpClientFactory(), Options.Create(new ParserSidecarOptions
            {
                BaseUrl = $"http://localhost:{_sidecarPort}"
            })),
            new RuleBasedSummarizer(),
            new RuleBasedSummarizer(),
            new FileSystemObjectStore(_objectRoot),
            new NoopGitHubAppClient(),
            new NoopOverviewService(),
            new NoopLinkingService(),
            new NoopEmbeddingGenerator(),
            new NullLogger<AnalysisPipeline>(),
            Options.Create(new AnalysisPipelineOptions { WorkRoot = _workRoot }),
            Options.Create(new AiOptions { ReviewThreshold = 0.7 }),
            Options.Create(new GitHubOptions()));
    }

    private sealed class NoopOverviewService : IOverviewService
    {
        public Task<OverviewResult> GenerateAsync(
            Repository repo,
            IReadOnlyList<KnowledgeNode> nodes,
            IReadOnlyList<GraphEdge>? edges = null,
            CancellationToken ct = default) =>
            Task.FromResult(new OverviewResult("", "none", 0, DateTimeOffset.UtcNow));
    }

    private sealed class NoopLinkingService : IArchitectureLinkingService
    {
        public Task<IReadOnlyList<LinkedEdge>> LinkAsync(ParseResult parse, long repositoryId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<LinkedEdge>>(Array.Empty<LinkedEdge>());
    }

    private sealed class NoopEmbeddingGenerator : IEmbeddingGenerator
    {
        public Task<int> GenerateAsync(
            Guid snapshotId,
            Guid repositoryId,
            IReadOnlyList<KnowledgeNode> nodes,
            CancellationToken ct = default,
            Guid? previousSnapshotId = null) =>
            Task.FromResult(0);
    }

    private TesseraDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<TesseraDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new TesseraDbContext(options);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Tessera.slnx")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new DirectoryNotFoundException("Repo root not found.");
    }

    private static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static Process StartSidecar(int port, string sidecarRoot)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "node",
            WorkingDirectory = sidecarRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.Environment["PORT"] = port.ToString();
        startInfo.ArgumentList.Add("src/index.js");
        return Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start sidecar.");
    }

    private void WaitForSidecar(int port)
    {
        using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}/") };
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                using var response = client.GetAsync("health").GetAwaiter().GetResult();
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    return;
                }
            }
            catch
            {
                // retry
            }
            Thread.Sleep(250);
        }
        var log = _sidecarLog.Length > 0 ? _sidecarLog.ToString().Trim() : "(no output captured)";
        throw new TimeoutException(
            $"Sidecar did not become healthy in time. Port {port}. Process output:{Environment.NewLine}{log}");
    }

    private static void InitGitRepo(string repoRoot)
    {
        RunGit(repoRoot, "init", "-b", "main");
        RunGit(repoRoot, "config", "user.email", "e2e@tessera.dev");
        RunGit(repoRoot, "config", "user.name", "E2E");
        WriteAll("Program.cs", """
            namespace Sample;

            public static class Program
            {
                public static void Main()
                {
                    var svc = new OrderService();
                    _ = svc.ApplyDiscount(0.1m);
                }
            }
            """);
        WriteAll("Order.cs", """
            namespace Sample;

            public class Order
            {
                public int Id { get; set; }
                public decimal Total { get; set; }
            }
            """);
        WriteAll("OrderService.cs", """
            namespace Sample;

            public class OrderService : Order
            {
                public decimal ApplyDiscount(decimal rate) => Total * (1 - rate);
            }
            """);
        RunGit(repoRoot, "add", ".");
        RunGit(repoRoot, "commit", "-m", "initial");

        void WriteAll(string name, string content) =>
            File.WriteAllText(Path.Combine(repoRoot, name), content);
    }

    private static void CommitSecondVersion(string repoRoot)
    {
        File.WriteAllText(Path.Combine(repoRoot, "Order.cs"), """
            namespace Sample;

            public class Order : IAuditable
            {
                public int Id { get; set; }
                public decimal Total { get; set; }
                public decimal Tax => Total * 0.19m;
            }
            """);
        File.WriteAllText(Path.Combine(repoRoot, "Auditable.cs"), """
            namespace Sample;

            public interface IAuditable { }
            """);
        File.WriteAllText(Path.Combine(repoRoot, "Payment.cs"), """
            namespace Sample;

            public class Payment
            {
                public Payment(Order order)
                {
                    Order = order;
                }

                public Order Order { get; set; }
                public bool Process() => Order.Total > 0;
            }
            """);
        RunGit(repoRoot, "add", ".");
        RunGit(repoRoot, "commit", "-m", "add payment");
    }

    private static void RunGit(string repoRoot, params string[] args)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = repoRoot,
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
            ?? throw new InvalidOperationException("Failed to start git.");
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"git {string.Join(' ', args)} failed: {stderr}");
        }
    }

    public void Dispose()
    {
        try
        {
            _sidecarProcess?.Kill(entireProcessTree: true);
            _sidecarProcess?.Dispose();
        }
        catch
        {
            // best effort
        }
        try
        {
            Directory.Delete(_workRoot, recursive: true);
        }
        catch
        {
            // best effort
        }
    }

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client = new(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        });
        public HttpClient CreateClient(string name) => _client;
    }

    private sealed class NoopGitHubAppClient : IGitHubAppClient
    {
        public Task<string> CreateInstallationAccessTokenAsync(long installationId, CancellationToken ct = default)
            => Task.FromResult("test-token");

        public Task<IReadOnlyList<GitHubRepoInfo>> ListInstallationRepositoriesAsync(long installationId, string token, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<GitHubRepoInfo>>(Array.Empty<GitHubRepoInfo>());

        public Task<long> PostPrCommentAsync(long installationId, string owner, string repo, int prNumber, string body, CancellationToken ct = default)
            => Task.FromResult(123L);

        public Task DeletePrCommentAsync(long installationId, string owner, string repo, long commentId, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
