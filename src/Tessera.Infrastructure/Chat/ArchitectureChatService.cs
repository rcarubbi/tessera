using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tessera.Domain.Entities;
using Tessera.Domain.Enums;
using Tessera.Domain.Ports;
using Tessera.Infrastructure.Ai;
using Tessera.Infrastructure.Data;
using Tessera.Infrastructure.Queries;

namespace Tessera.Infrastructure.Chat;

public enum ChatMode { Graph, Rag, NoContext }

public sealed record ChatCitation(string Key, string Symbol, string File, int Line, string Label);

public sealed record ChatResult(
    ChatMode Mode,
    string Answer,
    IReadOnlyList<ChatCitation> Citations,
    IReadOnlyList<string> Warnings);

public enum ChatStreamKind { Mode, Warnings, Delta, Citations }

public sealed record ChatStreamItem(
    ChatStreamKind Kind,
    ChatMode? Mode = null,
    IReadOnlyList<string>? Warnings = null,
    string? Text = null,
    IReadOnlyList<ChatCitation>? Citations = null);

public interface IArchitectureChatService
{
    Task<ChatResult> AnswerAsync(
        Guid repositoryId,
        string question,
        string? commitSha = null,
        int? topK = null,
        double? threshold = null,
        CancellationToken ct = default);

    IAsyncEnumerable<ChatStreamItem> AnswerStreamAsync(
        Guid repositoryId,
        string question,
        string? commitSha = null,
        int? topK = null,
        double? threshold = null,
        CancellationToken ct = default);
}

public sealed class ArchitectureChatService(
    TesseraDbContext db,
    GraphQueryService graph,
    IRetrievalService retrieval,
    IProviderRegistry providers,
    TokenBudgetTracker budget,
    IOptions<AiOptions> options) : IArchitectureChatService
{
    private const string SystemPrompt =
        """
        You are an expert software architect reverse-engineering legacy systems. Answer the user's
        question using ONLY the provided knowledge nodes extracted from the repository. When you refer
        to an entity, reference it by writing its exact KEY token in square brackets, e.g.
        [Order.cs::Order], and name its source file. Do not invent files, entities, or facts. If the
        provided nodes are insufficient, say so clearly. Be concise. Answer in the user's language.
        """;

    private static readonly Regex StructuralIntent = new(
        @"(?i)(impact|break|breaks|breaking|what happens|o que (quebra|acontece)|quem (usa|chama|consome)|who (uses|calls|consumes)|depend|used by|usado por|affected|afet|referenc|consumer|consumidor|dependenc)",
        RegexOptions.Compiled);

    private static readonly Regex ImpactIntent = new(
        @"(?i)(break|quebr|afet|impact|happen|acontece)",
        RegexOptions.Compiled);

    private static readonly Regex ConsumersIntent = new(
        @"(?i)(who (uses|calls|consumes)|quem (usa|chama|consome)|used by|usado por|consum|referenc)",
        RegexOptions.Compiled);

    private static readonly Regex DependenciesIntent = new(
        @"(?i)(depends? on|depende de|dependenc)",
        RegexOptions.Compiled);

    private static readonly Regex WordRegex = new(@"[\p{L}\p{N}_]+", RegexOptions.Compiled);

    private readonly AiOptions _options = options.Value;

    public async Task<ChatResult> AnswerAsync(
        Guid repositoryId,
        string question,
        string? commitSha = null,
        int? topK = null,
        double? threshold = null,
        CancellationToken ct = default)
    {
        var repo = await db.Repositories.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == repositoryId, ct)
            ?? throw new SnapshotNotFoundException(repositoryId, commitSha);

        var snapshot = await ResolveSnapshotAsync(repositoryId, commitSha, ct);
        if (snapshot is null)
        {
            return commitSha is null
                ? new ChatResult(ChatMode.NoContext,
                    "This repository has no analyzed snapshot yet.",
                    Array.Empty<ChatCitation>(), Array.Empty<string>())
                : throw new SnapshotNotFoundException(repositoryId, commitSha);
        }

        if (StructuralIntent.IsMatch(question))
        {
            var nodes = await db.KnowledgeNodes.AsNoTracking()
                .Where(n => n.SnapshotId == snapshot.Id)
                .ToListAsync(ct);
            var entity = MatchEntity(question, nodes);
            if (entity is not null)
            {
                return await AnswerFromGraphAsync(repositoryId, commitSha, question, entity, ct);
            }
        }

        return await AnswerFromRagAsync(repositoryId, commitSha, question, repo.GitHubId, topK, threshold, ct);
    }

    public async IAsyncEnumerable<ChatStreamItem> AnswerStreamAsync(
        Guid repositoryId,
        string question,
        string? commitSha = null,
        int? topK = null,
        double? threshold = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var repo = await db.Repositories.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == repositoryId, ct);
        if (repo is null)
        {
            throw new SnapshotNotFoundException(repositoryId, commitSha);
        }

        var snapshot = await ResolveSnapshotAsync(repositoryId, commitSha, ct);
        if (snapshot is null)
        {
            if (commitSha is not null)
            {
                throw new SnapshotNotFoundException(repositoryId, commitSha);
            }
            await foreach (var item in StreamResultAsync(NoContextResult(), ct))
            {
                yield return item;
            }
            yield break;
        }

        if (StructuralIntent.IsMatch(question))
        {
            var nodes = await db.KnowledgeNodes.AsNoTracking()
                .Where(n => n.SnapshotId == snapshot.Id)
                .ToListAsync(ct);
            var entity = MatchEntity(question, nodes);
            if (entity is not null)
            {
                var graphResult = await AnswerFromGraphAsync(repositoryId, commitSha, question, entity, ct);
                await foreach (var item in StreamResultAsync(graphResult, ct))
                {
                    yield return item;
                }
                yield break;
            }
        }

        var retrieved = await retrieval.RetrieveAsync(
            repositoryId,
            commitSha,
            question,
            topK ?? _options.TopK,
            threshold ?? _options.SimilarityThreshold,
            ct);

        if (retrieved.Count == 0)
        {
            await foreach (var item in StreamResultAsync(NoContextResult(), ct))
            {
                yield return item;
            }
            yield break;
        }

        yield return new ChatStreamItem(ChatStreamKind.Mode, Mode: ChatMode.Rag);
        yield return new ChatStreamItem(ChatStreamKind.Warnings, Warnings: WarningsFor(retrieved));

        var provider = providers.Primary;
        if (provider is null)
        {
            await foreach (var item in StreamResultBodyAsync(SynthesizeFromNodes(retrieved).Answer, retrieved, ct))
            {
                yield return item;
            }
            yield break;
        }

        var prompt = BuildRagPrompt(question, retrieved);
        var promptTokens = EstimateTokens(prompt) + 400;
        if (!budget.TryAllocate(repo.GitHubId, promptTokens, DateTimeOffset.UtcNow))
        {
            await foreach (var item in StreamResultBodyAsync(SynthesizeFromNodes(retrieved).Answer, retrieved, ct))
            {
                yield return item;
            }
            yield break;
        }

        var messages = new[] { new ChatMessage("system", SystemPrompt), new ChatMessage("user", prompt) };
        var (produced, answer) = await CollectProviderAnswerAsync(messages, provider, providers.Fallback, ct);

        if (!produced)
        {
            await foreach (var item in StreamResultBodyAsync(SynthesizeFromNodes(retrieved).Answer, retrieved, ct))
            {
                yield return item;
            }
            yield break;
        }

        var built = BuildRagResult(answer, retrieved);
        foreach (var chunk in Chunk(answer))
        {
            ct.ThrowIfCancellationRequested();
            yield return new ChatStreamItem(ChatStreamKind.Delta, Text: chunk);
        }
        yield return new ChatStreamItem(ChatStreamKind.Citations, Citations: built.Citations);
    }

    private async Task<(bool Produced, string Answer)> CollectProviderAnswerAsync(
        IReadOnlyList<ChatMessage> messages,
        IChatProvider primary,
        IChatProvider? fallback,
        CancellationToken ct)
    {
        var answer = await TryCollectAsync(primary, messages, ct);
        if (!string.IsNullOrWhiteSpace(answer))
        {
            return (true, answer);
        }
        if (fallback is not null)
        {
            answer = await TryCollectAsync(fallback, messages, ct);
            if (!string.IsNullOrWhiteSpace(answer))
            {
                return (true, answer);
            }
        }
        return (false, "");
    }

    private static async Task<string> TryCollectAsync(
        IChatProvider provider,
        IReadOnlyList<ChatMessage> messages,
        CancellationToken ct)
    {
        try
        {
            var sb = new StringBuilder();
            if (provider is IChatStreamProvider streamProvider)
            {
                await foreach (var token in streamProvider.StreamCompleteAsync(messages, ct))
                {
                    sb.Append(token);
                }
            }
            else
            {
                sb.Append(await provider.CompleteAsync(messages, ct));
            }
            return sb.ToString();
        }
        catch (Exception)
        {
            return "";
        }
    }

    private async IAsyncEnumerable<ChatStreamItem> StreamResultAsync(
        ChatResult result,
        [EnumeratorCancellation] CancellationToken ct)
    {
        yield return new ChatStreamItem(ChatStreamKind.Mode, Mode: result.Mode);
        foreach (var chunk in Chunk(result.Answer))
        {
            ct.ThrowIfCancellationRequested();
            yield return new ChatStreamItem(ChatStreamKind.Delta, Text: chunk);
        }
        yield return new ChatStreamItem(ChatStreamKind.Citations, Citations: result.Citations);
    }

    private async IAsyncEnumerable<ChatStreamItem> StreamResultBodyAsync(
        string answer,
        IReadOnlyList<RetrievedNode> retrieved,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var built = BuildRagResult(answer, retrieved);
        foreach (var chunk in Chunk(answer))
        {
            ct.ThrowIfCancellationRequested();
            yield return new ChatStreamItem(ChatStreamKind.Delta, Text: chunk);
        }
        yield return new ChatStreamItem(ChatStreamKind.Citations, Citations: built.Citations);
    }

    private ChatResult NoContextResult() => new(
        ChatMode.NoContext,
        "I couldn't find relevant context for this question in the current snapshot.",
        Array.Empty<ChatCitation>(),
        Array.Empty<string>());

    private static IEnumerable<string> Chunk(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            yield break;
        }
        const int size = 96;
        for (var i = 0; i < text.Length; i += size)
        {
            yield return text.Substring(i, Math.Min(size, text.Length - i));
        }
    }

    private async Task<ChatResult> AnswerFromGraphAsync(
        Guid repositoryId,
        string? commitSha,
        string question,
        KnowledgeNode entity,
        CancellationToken ct)
    {
        var sb = new StringBuilder();
        var citations = new List<ChatCitation> { Cite(entity) };

        if (ConsumersIntent.IsMatch(question) && !ImpactIntent.IsMatch(question))
        {
            var consumers = await graph.ConsumersAsync(repositoryId, entity.Key, commitSha, ct);
            if (consumers.Items.Count == 0)
            {
                sb.AppendLine($"`{entity.Symbol}` has no known consumers in the graph.");
            }
            else
            {
                sb.AppendLine($"`{entity.Symbol}` is referenced by:");
                foreach (var item in consumers.Items)
                {
                    sb.AppendLine($"- `{item.FromSymbol}` ({item.Path}:{item.Line}) — {item.Type.ToLowerInvariant()} (evidence: {item.Evidence ?? "graph"})");
                    citations.Add(new ChatCitation(item.FromKey, item.FromSymbol, item.Path, item.Line, $"{item.Path}:{item.Line}"));
                }
            }
        }
        else if (DependenciesIntent.IsMatch(question) && !ImpactIntent.IsMatch(question))
        {
            var chain = await graph.ChainAsync(repositoryId, entity.Key, commitSha, maxDepth: 10, ct);
            if (chain.Items.Count == 0)
            {
                sb.AppendLine($"`{entity.Symbol}` has no known direct dependencies in the graph.");
            }
            else
            {
                sb.AppendLine($"`{entity.Symbol}` depends on:");
                foreach (var item in chain.Items)
                {
                    sb.AppendLine($"- `{item.Symbol}` ({item.Path}:{item.Line}) — via {item.Type.ToLowerInvariant()}");
                    citations.Add(new ChatCitation(item.Key, item.Symbol, item.Path, item.Line, $"{item.Path}:{item.Line}"));
                }
            }
        }
        else
        {
            var impact = await graph.ImpactAsync(repositoryId, entity.Key, commitSha, maxDepth: 10, ct);
            if (impact.Items.Count == 0)
            {
                sb.AppendLine($"Changing `{entity.Symbol}` would affect no known dependents in the graph.");
            }
            else
            {
                sb.AppendLine($"Changing `{entity.Symbol}` may affect:");
                foreach (var item in impact.Items)
                {
                    sb.AppendLine($"- `{item.Symbol}` ({item.Path}:{item.Line}) — {item.Severity} impact (depth {item.Depth})");
                    citations.Add(new ChatCitation(item.Key, item.Symbol, item.Path, item.Line, $"{item.Path}:{item.Line}"));
                }
            }
        }

        return new ChatResult(ChatMode.Graph, sb.ToString().TrimEnd(), citations, Array.Empty<string>());
    }

    private async Task<ChatResult> AnswerFromRagAsync(
        Guid repositoryId,
        string? commitSha,
        string question,
        long gitHubId,
        int? topK,
        double? threshold,
        CancellationToken ct)
    {
        var retrieved = await retrieval.RetrieveAsync(
            repositoryId,
            commitSha,
            question,
            topK ?? _options.TopK,
            threshold ?? _options.SimilarityThreshold,
            ct);

        if (retrieved.Count == 0)
        {
            return new ChatResult(ChatMode.NoContext,
                "I couldn't find relevant context for this question in the current snapshot.",
                Array.Empty<ChatCitation>(), Array.Empty<string>());
        }

        var provider = providers.Primary;
        if (provider is null)
        {
            return SynthesizeFromNodes(retrieved);
        }

        var prompt = BuildRagPrompt(question, retrieved);
        var promptTokens = EstimateTokens(prompt) + 400;
        if (budget.TryAllocate(gitHubId, promptTokens, DateTimeOffset.UtcNow))
        {
            var messages = new[] { new ChatMessage("system", SystemPrompt), new ChatMessage("user", prompt) };
            try
            {
                var answer = await RetryPolicy.WithRetryAsync(ct2 => provider.CompleteAsync(messages, ct2), _options.MaxRetries, ct: ct);
                if (!string.IsNullOrWhiteSpace(answer))
                {
                    return BuildRagResult(answer, retrieved);
                }
            }
            catch (Exception) when (providers.Fallback is not null)
            {
                try
                {
                    var answer = await providers.Fallback.CompleteAsync(messages, ct);
                    if (!string.IsNullOrWhiteSpace(answer))
                    {
                        return BuildRagResult(answer, retrieved);
                    }
                }
                catch (Exception)
                {
                }
            }
            catch (Exception)
            {
            }
        }

        return SynthesizeFromNodes(retrieved);
    }

    private ChatResult BuildRagResult(string answer, IReadOnlyList<RetrievedNode> retrieved)
    {
        var citations = new List<ChatCitation>();
        foreach (var node in retrieved.Select(r => r.Node))
        {
            if (answer.Contains(node.Key, StringComparison.OrdinalIgnoreCase)
                || (node.Symbol.Length >= 3 && answer.Contains(node.Symbol, StringComparison.OrdinalIgnoreCase)))
            {
                citations.Add(Cite(node));
            }
        }
        if (citations.Count == 0)
        {
            var top = retrieved[0].Node;
            citations.Add(Cite(top));
            answer += $"\n\nTop relevant node: {top.Path}:{top.StartLine}";
        }
        return new ChatResult(ChatMode.Rag, answer, citations, WarningsFor(retrieved));
    }

    private ChatResult SynthesizeFromNodes(IReadOnlyList<RetrievedNode> retrieved)
    {
        var top = retrieved[0].Node;
        var sb = new StringBuilder();
        sb.AppendLine("Semantic answer unavailable (no AI provider configured). Top relevant context for your question:");
        sb.AppendLine();
        sb.AppendLine(top.Content.Trim());
        return new ChatResult(ChatMode.Rag, sb.ToString(), new[] { Cite(top) }, WarningsFor(retrieved));
    }

    private List<string> WarningsFor(IReadOnlyList<RetrievedNode> retrieved)
    {
        var warnings = new List<string>();
        foreach (var node in retrieved.Select(r => r.Node).DistinctBy(n => n.Key))
        {
            if (node.ReviewStatus != ReviewStatus.None || node.Confidence < _options.ReviewThreshold)
            {
                warnings.Add($"Node `{node.Symbol}` is flagged {ReviewLabel(node.ReviewStatus)} (confidence {node.Confidence:F2}) and may need review.");
            }
        }
        return warnings;
    }

    private static string BuildRagPrompt(string question, IReadOnlyList<RetrievedNode> retrieved)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"User question: {question}");
        sb.AppendLine();
        sb.AppendLine($"Knowledge nodes retrieved from the repository:");
        foreach (var node in retrieved.Select(r => r.Node))
        {
            sb.AppendLine();
            sb.AppendLine($"--- [{node.Key}]");
            sb.AppendLine($"Source: {node.Path} lines {node.StartLine}-{node.EndLine} | confidence {node.Confidence:F2} | review: {ReviewLabel(node.ReviewStatus)}");
            var content = node.Content;
            if (content.Length > 1600)
            {
                content = content[..1600] + "... [truncated]";
            }
            sb.AppendLine(content);
        }
        return sb.ToString();
    }

    private static ChatCitation Cite(KnowledgeNode node) =>
        new(node.Key, node.Symbol, node.Path, node.StartLine, $"{node.Path}:{node.StartLine}");

    private static string ReviewLabel(ReviewStatus status) => status switch
    {
        ReviewStatus.NeedsReview => "needs review",
        ReviewStatus.Stale => "stale",
        ReviewStatus.Accepted => "accepted",
        ReviewStatus.Edited => "edited",
        _ => "unreviewed"
    };

    private static KnowledgeNode? MatchEntity(string question, List<KnowledgeNode> nodes)
    {
        var words = WordRegex.Matches(question.ToLowerInvariant())
            .Select(m => m.Value)
            .ToHashSet(StringComparer.Ordinal);

        var bySymbol = nodes.FirstOrDefault(n => words.Contains(n.Symbol.ToLowerInvariant()));
        if (bySymbol is not null)
        {
            return bySymbol;
        }

        var lowered = question.ToLowerInvariant();
        var byPath = nodes.FirstOrDefault(n => n.Path.Length > 3 && lowered.Contains(n.Path.ToLowerInvariant()));
        if (byPath is not null)
        {
            return byPath;
        }

        return nodes.FirstOrDefault(n => lowered.Contains(n.Key.ToLowerInvariant()));
    }

    private async Task<Snapshot?> ResolveSnapshotAsync(Guid repositoryId, string? commitSha, CancellationToken ct)
    {
        var query = db.Snapshots.AsNoTracking().Where(s => s.RepositoryId == repositoryId);
        if (!string.IsNullOrEmpty(commitSha))
        {
            query = query.Where(s => s.CommitSha == commitSha);
        }
        return await query.OrderByDescending(s => s.CreatedAt).FirstOrDefaultAsync(ct);
    }

    private static long EstimateTokens(string text) => (text.Length + 3) / 4;
}
