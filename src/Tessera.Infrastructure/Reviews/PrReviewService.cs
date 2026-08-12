using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Tessera.Domain.Entities;
using Tessera.Domain.Enums;
using Tessera.Domain.Ports;
using Tessera.Infrastructure.Ai;
using Tessera.Infrastructure.Analysis;
using Tessera.Infrastructure.Data;
using Tessera.Infrastructure.GitHub;
using Tessera.Infrastructure.Queries;

namespace Tessera.Infrastructure.Reviews;

public sealed record PrReviewItem(
    Guid Id,
    int PrNumber,
    string HeadSha,
    string BaseSha,
    string Status,
    long? CommentId,
    string? CommentBody,
    string? ErrorMessage,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record PrReviewListResult(IReadOnlyList<PrReviewItem> Items);

public sealed record PrImpactItem(string Symbol, string Path, int Line, string Severity);
public sealed record PrNewEdge(string From, string To, string Type);

public sealed record PrReport(
    string HeadSha,
    string BaseSha,
    IReadOnlyList<string> ChangedFiles,
    IReadOnlyList<PrImpactItem> Impact,
    IReadOnlyList<PrNewEdge> NewEdges,
    bool EdgeDeltaUnavailable,
    IReadOnlyList<RuleViolation> Violations,
    bool RulesEvaluated,
    string? AiSummary,
    IReadOnlyList<string> Notes);

public sealed class PrReviewService(
    TesseraDbContext db,
    GraphQueryService graph,
    ArchitectureRuleService rules,
    IGitHubAppClient github,
    IProviderRegistry providers,
    IGitClient git,
    ILogger<PrReviewService> logger)
{
    private const int MaxChangedFilesListed = 20;
    private const int MaxImpactEntities = 30;

    public async Task<PrReviewListResult> ListAsync(Guid repositoryId, CancellationToken ct = default)
    {
        var items = await db.PullRequestReviews.AsNoTracking()
            .Where(r => r.RepositoryId == repositoryId)
            .OrderByDescending(r => r.UpdatedAt)
            .Select(r => new PrReviewItem(
                r.Id, r.PrNumber, r.HeadSha, r.BaseSha, r.Status.ToString(),
                r.CommentId, r.CommentBody, r.ErrorMessage, r.CreatedAt, r.UpdatedAt))
            .ToListAsync(ct);
        return new PrReviewListResult(items);
    }

    public async Task ProcessAsync(Repository repo, PullRequestReview review, string workDir, CancellationToken ct = default)
    {
        if (review.Status is not (PrReviewStatus.Queued or PrReviewStatus.Failed))
        {
            return;
        }

        var headSnapshot = await db.Snapshots.AsNoTracking()
            .Where(s => s.RepositoryId == repo.Id && s.CommitSha == review.HeadSha)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (headSnapshot is null)
        {
            // Gating: head snapshot not ready yet; stays Queued until one matches.
            return;
        }

        PrReport report;
        try
        {
            report = await ComputeReportAsync(repo, review, headSnapshot, workDir, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "PR report computation failed for #{pr} in {repo}", review.PrNumber, repo.FullName);
            review.Status = PrReviewStatus.Failed;
            review.ErrorMessage = TrimError(ex.Message);
            review.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return;
        }

        review.CommentBody = Render(report);
        review.ErrorMessage = null;

        if (!repo.EnablePrComments)
        {
            review.Status = PrReviewStatus.Reviewed;
            review.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return;
        }

        try
        {
            await DeletePreviousCommentAsync(repo, review, ct);
            var commentId = await RetryPolicy.WithRetryAsync(
                ct2 => github.PostPrCommentAsync(repo.InstallationId, repo.Owner, repo.Name, review.PrNumber, review.CommentBody, ct2),
                maxRetries: 2,
                ct: ct);
            review.CommentId = commentId;
            review.Status = PrReviewStatus.Posted;
            logger.LogInformation("Posted PR comment {commentId} for #{pr} in {repo}", commentId, review.PrNumber, repo.FullName);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "PR comment post failed for #{pr} in {repo}", review.PrNumber, repo.FullName);
            review.Status = PrReviewStatus.Failed;
            review.ErrorMessage = TrimError(ex.Message);
        }
        review.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private async Task<PrReport> ComputeReportAsync(
        Repository repo,
        PullRequestReview review,
        Snapshot headSnapshot,
        string workDir,
        CancellationToken ct)
    {
        var changedFiles = await GetChangedFilesAsync(workDir, review.BaseSha, review.HeadSha, ct);
        var impact = await BuildImpactAsync(repo.Id, headSnapshot.CommitSha, headSnapshot.Id, changedFiles, ct);

        var (newEdges, edgeDeltaUnavailable) = await BuildEdgeDeltaAsync(repo.Id, review.BaseSha, headSnapshot.CommitSha, ct);
        var (violations, rulesEvaluated) = await BuildViolationsAsync(repo.Id, headSnapshot.CommitSha, ct);
        var aiSummary = await BuildAiSummaryAsync(repo.Id, headSnapshot.CommitSha, changedFiles, impact, newEdges, ct);

        var notes = new List<string>();
        if (edgeDeltaUnavailable)
        {
            notes.Add("Base snapshot not available; new-dependency section omitted.");
        }
        if (!rulesEvaluated)
        {
            notes.Add("No architecture rules configured; rules section omitted.");
        }
        if (aiSummary is null)
        {
            notes.Add("AI summary unavailable.");
        }

        return new PrReport(
            review.HeadSha,
            review.BaseSha,
            changedFiles,
            impact,
            newEdges,
            edgeDeltaUnavailable,
            violations,
            rulesEvaluated,
            aiSummary,
            notes);
    }

    private async Task<IReadOnlyList<string>> GetChangedFilesAsync(string workDir, string baseSha, string headSha, CancellationToken ct)
    {
        try
        {
            var files = await git.GetChangedFilesAsync(workDir, baseSha, headSha, ct);
            return files.Where(HasSupportedExtension).ToList();
        }
        catch (GitCommandException ex)
        {
            logger.LogWarning(ex, "Could not resolve changed files for PR {base}..{head}", baseSha, headSha);
            return Array.Empty<string>();
        }
    }

    private async Task<IReadOnlyList<PrImpactItem>> BuildImpactAsync(
        Guid repositoryId,
        string headSha,
        Guid snapshotId,
        IReadOnlyList<string> changedFiles,
        CancellationToken ct)
    {
        if (changedFiles.Count == 0)
        {
            return Array.Empty<PrImpactItem>();
        }

        var entityKeys = await db.KnowledgeNodes.AsNoTracking()
            .Where(n => n.SnapshotId == snapshotId)
            .Select(n => new { n.Key, n.Path })
            .ToListAsync(ct);

        var candidates = entityKeys
            .Where(n => changedFiles.Any(f => n.Path.StartsWith(f, StringComparison.OrdinalIgnoreCase)))
            .Take(MaxImpactEntities)
            .ToList();

        var impactByKey = new Dictionary<string, PrImpactItem>(StringComparer.Ordinal);
        foreach (var candidate in candidates)
        {
            var impact = await graph.ImpactAsync(repositoryId, candidate.Key, headSha, maxDepth: 3, ct);
            foreach (var item in impact.Items)
            {
                impactByKey.TryAdd(item.Key, new PrImpactItem(item.Symbol, item.Path, item.Line, item.Severity));
            }
        }

        return impactByKey.Values
            .OrderBy(i => i.Path, StringComparer.Ordinal)
            .ThenBy(i => i.Line)
            .ToList();
    }

    private async Task<(IReadOnlyList<PrNewEdge> Edges, bool Unavailable)> BuildEdgeDeltaAsync(
        Guid repositoryId,
        string baseSha,
        string headSha,
        CancellationToken ct)
    {
        try
        {
            var diff = await graph.DiffAsync(repositoryId, baseSha, headSha, ct);
            var added = diff.Edges.Where(e => e.Change == "added")
                .Select(e => new PrNewEdge(e.From, e.To, e.Type))
                .ToList();
            return (added, false);
        }
        catch (SnapshotNotFoundException)
        {
            return (Array.Empty<PrNewEdge>(), true);
        }
    }

    private async Task<(IReadOnlyList<RuleViolation> Violations, bool Evaluated)> BuildViolationsAsync(
        Guid repositoryId,
        string headSha,
        CancellationToken ct)
    {
        var yaml = await db.Repositories.AsNoTracking()
            .Where(r => r.Id == repositoryId)
            .Select(r => r.RulesYaml)
            .FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(yaml))
        {
            return (Array.Empty<RuleViolation>(), false);
        }

        var ruleSet = ArchitectureRuleService.Parse(yaml);
        var result = await rules.EvaluateAsync(repositoryId, ruleSet, headSha, ct);
        return (result.Violations, true);
    }

    private async Task<string?> BuildAiSummaryAsync(
        Guid repositoryId,
        string headSha,
        IReadOnlyList<string> changedFiles,
        IReadOnlyList<PrImpactItem> impact,
        IReadOnlyList<PrNewEdge> newEdges,
        CancellationToken ct)
    {
        try
        {
            var provider = providers.Primary;
            if (provider is null)
            {
                return null;
            }

            var user = new StringBuilder();
            user.AppendLine("Changed files:");
            user.AppendLine(changedFiles.Count == 0 ? "- none" : string.Join(Environment.NewLine, changedFiles.Select(f => $"- `{f}`")));
            user.AppendLine();
            user.AppendLine("Impacted dependents (union across changed files):");
            user.AppendLine(impact.Count == 0 ? "- none" : string.Join(Environment.NewLine, impact.Select(i => $"- `{i.Symbol}` (`{i.Path}:{i.Line}`)")));
            user.AppendLine();
            user.AppendLine("New dependencies (base → head):");
            user.AppendLine(newEdges.Count == 0 ? "- none" : string.Join(Environment.NewLine, newEdges.Select(e => $"- `{e.From}` → `{e.To}` ({e.Type})")));

            var content = await RetryPolicy.WithRetryAsync(
                ct2 => provider.CompleteAsync(
                [
                    new ChatMessage("system", "You are Tessera, an automated code-architecture analyzer. Write ONE concise paragraph (3-4 sentences, plain markdown, no preamble) describing what this pull request changes architecturally: new or touched components, dependents affected, and dependency relationships introduced. If the data is thin, say so in one short clause. Do not invent facts not present in the input."),
                    new ChatMessage("user", user.ToString())
                ], ct2),
                maxRetries: 2,
                ct: ct);

            var trimmed = content.Trim();
            return trimmed.Length == 0 ? null : trimmed;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "AI PR summary failed for head {sha}; omitting section.", headSha);
            return null;
        }
    }

    private async Task DeletePreviousCommentAsync(Repository repo, PullRequestReview review, CancellationToken ct)
    {
        var previous = await db.PullRequestReviews.AsNoTracking()
            .Where(r => r.RepositoryId == repo.Id
                && r.PrNumber == review.PrNumber
                && r.HeadSha != review.HeadSha
                && r.CommentId != null)
            .OrderByDescending(r => r.UpdatedAt)
            .FirstOrDefaultAsync(ct);
        if (previous?.CommentId is not long commentId)
        {
            return;
        }
        try
        {
            await github.DeletePrCommentAsync(repo.InstallationId, repo.Owner, repo.Name, commentId, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not delete previous PR comment {commentId}; continuing.", commentId);
        }
    }

    public static string Render(PrReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Tessera PR analysis");
        sb.AppendLine();
        sb.AppendLine($"**Head** `{report.HeadSha}` · **Base** `{report.BaseSha}`");
        sb.AppendLine();

        sb.AppendLine("### Impact");
        sb.AppendLine();
        if (report.ChangedFiles.Count == 0)
        {
            sb.AppendLine("No changed files detected for the base→head range.");
        }
        else
        {
            var listed = report.ChangedFiles.Take(MaxChangedFilesListed).ToList();
            sb.Append("Changed files: ");
            sb.Append(string.Join(", ", listed.Select(f => $"`{f}`")));
            if (report.ChangedFiles.Count > MaxChangedFilesListed)
            {
                sb.Append($" and {report.ChangedFiles.Count - MaxChangedFilesListed} more");
            }
            sb.AppendLine();
            sb.AppendLine();
            if (report.Impact.Count == 0)
            {
                sb.AppendLine("No impacted dependents found for the changed files.");
            }
            else
            {
                sb.AppendLine($"{report.Impact.Count} impacted node(s) across the changed files (union, may overlap):");
                foreach (var item in report.Impact)
                {
                    sb.AppendLine($"- `{item.Symbol}` (`{item.Path}:{item.Line}`) — {item.Severity}");
                }
            }
        }
        sb.AppendLine();

        sb.AppendLine("### New dependencies");
        sb.AppendLine();
        if (report.EdgeDeltaUnavailable)
        {
            sb.AppendLine("Not computed — base snapshot unavailable.");
        }
        else if (report.NewEdges.Count == 0)
        {
            sb.AppendLine("No new dependencies between base and head.");
        }
        else
        {
            foreach (var edge in report.NewEdges)
            {
                sb.AppendLine($"- `{edge.From}` → `{edge.To}` — {edge.Type}");
            }
        }
        sb.AppendLine();

        sb.AppendLine("### Architecture rules");
        sb.AppendLine();
        if (!report.RulesEvaluated)
        {
            sb.AppendLine("Not evaluated — no rules configured.");
        }
        else if (report.Violations.Count == 0)
        {
            sb.AppendLine("No violations at head.");
        }
        else
        {
            foreach (var violation in report.Violations)
            {
                sb.AppendLine($"- `[{violation.Severity.ToString().ToLowerInvariant()}]` {violation.RuleName}: `{violation.FromKey}` → `{violation.ToKey}`");
            }
        }
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(report.AiSummary))
        {
            sb.AppendLine("### Summary");
            sb.AppendLine();
            sb.AppendLine(report.AiSummary);
            sb.AppendLine();
        }

        if (report.Notes.Count > 0)
        {
            sb.AppendLine("---");
            foreach (var note in report.Notes)
            {
                sb.AppendLine($"> {note}");
            }
        }

        return sb.ToString().TrimEnd();
    }

    private static bool HasSupportedExtension(string path)
    {
        var ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        return ext is "cs" or "java" or "js" or "jsx" or "mjs" or "cjs" or "ts" or "tsx" or "py" or "go" or "php" or "rb";
    }

    private static string TrimError(string? message)
    {
        var value = string.IsNullOrWhiteSpace(message) ? "Unknown error" : message;
        return value.Length <= 2000 ? value : value[..2000];
    }
}
