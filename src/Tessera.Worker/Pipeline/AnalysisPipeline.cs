using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tessera.Domain.Entities;
using Tessera.Domain.Enums;
using Tessera.Domain.Merkle;
using Tessera.Domain.Parsing;
using Tessera.Domain.Ports;
using Tessera.Infrastructure.Ai;
using Tessera.Infrastructure.Analysis;
using Tessera.Infrastructure.Chat;
using Tessera.Infrastructure.Data;
using Tessera.Infrastructure.GitHub;

namespace Tessera.Worker.Pipeline;

public sealed class AnalysisPipelineOptions
{
    public string WorkRoot { get; set; } = "work";
    public int MaxFilesPerBatch { get; set; } = 400;
}

public sealed class AnalysisPipeline(
    TesseraDbContext db,
    IGitClient git,
    IParserSidecarClient parser,
    ISemanticSummarizer summarizer,
    IObjectStore store,
    IGitHubAppClient github,
    IOverviewService overviewService,
    IOptions<AnalysisPipelineOptions> options,
    IOptions<AiOptions> aiOptions,
    IOptions<GitHubOptions> githubOptions)
{
    private readonly string _workRoot = Path.Combine(options.Value.WorkRoot, "repos");
    private readonly AiOptions _aiOptions = aiOptions.Value;

    public async Task ProcessAsync(Repository repo, CancellationToken ct = default)
    {
        var workDir = Path.Combine(_workRoot, repo.FullName);

        repo.Status = ProcessingStatus.Cloning;
        await db.SaveChangesAsync(ct);

        try
        {
            var defaultBranch = await git.EnsureCloneAsync(await ResolveCloneUrlAsync(repo, ct), workDir, ct);
            var head = await git.ResolveHeadAsync(workDir, defaultBranch, ct);

            if (string.Equals(head, repo.LastProcessedCommit, StringComparison.OrdinalIgnoreCase))
            {
                repo.Status = ProcessingStatus.Completed;
                repo.UpdatedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(ct);
                return;
            }

            repo.Status = ProcessingStatus.Parsing;
            repo.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);

            var parse = await ParseRepositoryAsync(workDir, repo, head, ct);

            repo.Status = ProcessingStatus.Analyzing;
            await db.SaveChangesAsync(ct);

            var previousNodes = await LoadPreviousNodesAsync(repo, ct);
            var aiContent = await BuildAiContentAsync(parse, previousNodes, repo, ct);

            var snapshotId = Guid.NewGuid();
            var composed = SnapshotComposer.Compose(
                repo.Id,
                snapshotId,
                head,
                parse,
                previousNodes,
                aiContent);

            repo.Status = ProcessingStatus.Indexing;
            await db.SaveChangesAsync(ct);

            foreach (var node in composed.Nodes)
            {
                if (node.ReviewStatus == ReviewStatus.None
                    && node.Confidence < _aiOptions.ReviewThreshold)
                {
                    node.ReviewStatus = ReviewStatus.NeedsReview;
                }
            }

            await PersistAsync(repo, snapshotId, head, composed, aiContent, ct);
            await GenerateOverviewAsync(repo, snapshotId, composed, ct);

            repo.Status = ProcessingStatus.Completed;
            repo.LastProcessedCommit = head;
            repo.NodeCount = composed.Nodes.Count;
            repo.EdgeCount = composed.Edges.Count;
            repo.LastSnapshotAt = DateTimeOffset.UtcNow;
            repo.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            db.ChangeTracker.Clear();
            db.Attach(repo);
            repo.Status = ProcessingStatus.Failed;
            repo.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            throw new AnalysisPipelineException($"Analysis of {repo.FullName} failed: {ex.Message}", ex);
        }
    }

    private async Task<string> ResolveCloneUrlAsync(Repository repo, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(repo.CloneUrl))
        {
            return repo.CloneUrl ?? "";
        }
        if (!repo.CloneUrl.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase)
            || repo.InstallationId <= 0
            || string.IsNullOrEmpty(githubOptions.Value.AppId))
        {
            return repo.CloneUrl;
        }

        var token = await github.CreateInstallationAccessTokenAsync(repo.InstallationId, ct);
        var uri = new Uri(repo.CloneUrl);
        return $"https://x-access-token:{token}@{uri.Host}{uri.PathAndQuery}";
    }

    private async Task<ParseResult> ParseRepositoryAsync(string workDir, Repository repo, string head, CancellationToken ct)
    {
        var files = await git.ListFilesAtCommitAsync(workDir, head, ct);
        var sourceFiles = new List<ParsedSourceFile>();

        foreach (var file in files.Take(options.Value.MaxFilesPerBatch))
        {
            if (!HasSupportedExtension(file))
            {
                continue;
            }
            var content = await git.ReadFileAtCommitAsync(workDir, head, file, ct);
            if (content is not null)
            {
                sourceFiles.Add(new ParsedSourceFile(file, content));
            }
        }

        return await parser.ParseAsync(head, repo.DefaultBranch, sourceFiles, ct);
    }

    private async Task<Dictionary<string, KnowledgeNode>> LoadPreviousNodesAsync(Repository repo, CancellationToken ct)
    {
        var commit = repo.LastProcessedCommit;
        if (commit is null)
        {
            // Manual reprocess clears LastProcessedCommit to bypass the skip check,
            // but we still want to diff against the latest snapshot so unchanged
            // nodes are not re-sent to the LLM.
            commit = await db.Snapshots.AsNoTracking()
                .Where(s => s.RepositoryId == repo.Id)
                .OrderByDescending(s => s.CreatedAt)
                .Select(s => s.CommitSha)
                .FirstOrDefaultAsync(ct);
        }

        if (commit is null)
        {
            return new Dictionary<string, KnowledgeNode>(StringComparer.Ordinal);
        }

        var previousSnapshot = await db.Snapshots
            .Where(s => s.RepositoryId == repo.Id && s.CommitSha == commit)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (previousSnapshot is null)
        {
            return new Dictionary<string, KnowledgeNode>(StringComparer.Ordinal);
        }

        var nodes = await db.KnowledgeNodes
            .AsNoTracking()
            .Where(n => n.SnapshotId == previousSnapshot.Id)
            .ToListAsync(ct);

        return nodes.ToDictionary(n => n.Key, n => n, StringComparer.Ordinal);
    }

    private async Task<Dictionary<string, AiContent>> BuildAiContentAsync(
        ParseResult parse,
        IReadOnlyDictionary<string, KnowledgeNode> previousNodes,
        Repository repo,
        CancellationToken ct)
    {
        var aiContent = new Dictionary<string, AiContent>(StringComparer.Ordinal);
        foreach (var entity in parse.Entities)
        {
            var needsAi = !previousNodes.TryGetValue(entity.Key, out var previous)
                || previous.StructuralHash != entity.StructuralHash
                || previous.PromptVersion != summarizer.PromptVersion;

            if (needsAi)
            {
                var relationships = parse.Relationships
                    .Where(r => r.From == entity.Key || r.To == entity.Key)
                    .ToList();
                aiContent[entity.Key] = await summarizer.SummarizeAsync(entity, relationships, repo.GitHubId, ct);
            }
        }
        return aiContent;
    }

    private async Task PersistAsync(
        Repository repo,
        Guid snapshotId,
        string head,
        ComposedSnapshot composed,
        IReadOnlyDictionary<string, AiContent> aiContent,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var priorSnapshotIds = await db.Snapshots
            .Where(s => s.RepositoryId == repo.Id && s.CommitSha == head)
            .Select(s => s.Id)
            .ToListAsync(ct);
        foreach (var priorId in priorSnapshotIds)
        {
            var priorNodeIds = db.KnowledgeNodes.Where(n => n.SnapshotId == priorId).Select(n => n.Id);
            await db.KnowledgeNodeProvenances.Where(p => priorNodeIds.Contains(p.NodeId)).ExecuteDeleteAsync(ct);
            await db.KnowledgeNodes.Where(n => n.SnapshotId == priorId).ExecuteDeleteAsync(ct);
            await db.GraphEdges.Where(e => e.SnapshotId == priorId).ExecuteDeleteAsync(ct);
            await db.Snapshots.Where(s => s.Id == priorId).ExecuteDeleteAsync(ct);
        }

        var snapshot = new Snapshot
        {
            Id = snapshotId,
            RepositoryId = repo.Id,
            CommitSha = head,
            RootHash = composed.RootHash,
            NodeCount = composed.Nodes.Count,
            EdgeCount = composed.Edges.Count,
            ParentCommitSha = repo.LastProcessedCommit,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.Snapshots.Add(snapshot);

        var keysWritten = new HashSet<string>(StringComparer.Ordinal);
        var provisoryNodes = new List<KnowledgeNode>(composed.Nodes.Count);
        var provisoryEdges = new List<GraphEdge>(composed.Edges.Count);
        var provenances = new List<KnowledgeNodeProvenance>();

        foreach (var node in composed.Nodes)
        {
            db.KnowledgeNodes.Add(node);
            keysWritten.Add(node.Key);
            provisoryNodes.Add(node);
        }

        foreach (var edge in composed.Edges)
        {
            db.GraphEdges.Add(edge);
            provisoryEdges.Add(edge);
        }

        foreach (var kvp in aiContent)
        {
            if (!keysWritten.Contains(kvp.Key))
            {
                continue;
            }
            provenances.Add(new KnowledgeNodeProvenance
            {
                Id = Guid.NewGuid(),
                NodeId = composed.Nodes.First(n => n.Key == kvp.Key).Id,
                CommitSha = head,
                Model = kvp.Value.Model,
                PromptVersion = kvp.Value.PromptVersion,
                GeneratedAt = DateTimeOffset.UtcNow
            });
        }

        db.KnowledgeNodeProvenances.AddRange(provenances);

        var snapshotJson = JsonSerializer.Serialize(new StoredSnapshot
        {
            CommitSha = head,
            RootHash = composed.RootHash,
            Nodes = composed.Nodes.Select(n => new StoredNode
            {
                Key = n.Key,
                Symbol = n.Symbol,
                Kind = n.Kind.ToString(),
                Content = n.Content,
                StructuralHash = n.StructuralHash,
                SemanticHash = n.SemanticHash,
                Confidence = n.Confidence
            }).ToList(),
            Edges = composed.Edges.Select(e => new StoredEdge
            {
                From = e.FromKey,
                To = e.ToKey,
                Type = e.Type.ToString(),
                Evidence = e.Evidence,
                Confidence = e.Confidence
            }).ToList()
        });
        await store.PutAsync($"snapshots/{composed.RootHash}.json", snapshotJson, ct);

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    private async Task GenerateOverviewAsync(
        Repository repo,
        Guid snapshotId,
        ComposedSnapshot composed,
        CancellationToken ct)
    {
        try
        {
            var result = await overviewService.GenerateAsync(repo, composed.Nodes, ct);
            db.ProjectOverviews.Add(new ProjectOverview
            {
                Id = Guid.NewGuid(),
                RepositoryId = repo.Id,
                SnapshotId = snapshotId,
                Content = result.Overview,
                Model = result.Model,
                NodeCount = result.NodeCount,
                GeneratedAt = result.GeneratedAt
            });
            await db.SaveChangesAsync(ct);
        }
        catch (Exception)
        {
            // Overview generation must never fail the analysis.
        }
    }

    private static bool HasSupportedExtension(string path)
    {
        var ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        return ext is "cs" or "java" or "js" or "jsx" or "mjs" or "cjs" or "ts" or "tsx" or "py" or "go" or "php" or "rb";
    }
}

public sealed class StoredSnapshot
{
    public string CommitSha { get; set; } = "";
    public string RootHash { get; set; } = "";
    public List<StoredNode> Nodes { get; set; } = new();
    public List<StoredEdge> Edges { get; set; } = new();
}

public sealed class StoredNode
{
    public string Key { get; set; } = "";
    public string Symbol { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Content { get; set; } = "";
    public string StructuralHash { get; set; } = "";
    public string SemanticHash { get; set; } = "";
    public double Confidence { get; set; }
}

public sealed class StoredEdge
{
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public string Type { get; set; } = "";
    public string? Evidence { get; set; }
    public double Confidence { get; set; }
}

public sealed class AnalysisPipelineException(string message, Exception inner) : Exception(message, inner);
