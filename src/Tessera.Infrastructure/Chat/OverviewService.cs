using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tessera.Domain.Entities;
using Tessera.Domain.Ports;
using Tessera.Infrastructure.Ai;
using Tessera.Infrastructure.Data;

namespace Tessera.Infrastructure.Chat;

public sealed record OverviewResult(
    string Overview,
    string Model,
    int NodeCount,
    DateTimeOffset GeneratedAt);

public interface IOverviewService
{
    Task<OverviewResult> GenerateAsync(
        Repository repo,
        IReadOnlyList<KnowledgeNode> nodes,
        CancellationToken ct = default);
}

public sealed class OverviewService(
    IProviderRegistry providers,
    TokenBudgetTracker budget,
    IOptions<AiOptions> options) : IOverviewService
{
    private const int MaxNodes = 300;

    private static readonly Regex RoleRegex = new(
        @"(?im)^\s*[-*]\s*(?:Architecture|Role)\s*:\s*(.+)$",
        RegexOptions.Compiled);

    private static readonly Regex BoundedContextRegex = new(
        @"(?im)^\s*[-*]\s*Bounded\s*context\s*:\s*(.+)$",
        RegexOptions.Compiled);

    private static readonly Regex ResponsibilityRegex = new(
        @"(?im)^\s*-\s+(.+)$",
        RegexOptions.Compiled);

    private static readonly Regex CodeFenceRegex = new(
        @"```(?:\w+)?\s*(.*?)```",
        RegexOptions.Singleline);

    private readonly AiOptions _options = options.Value;

    public async Task<OverviewResult> GenerateAsync(
        Repository repo,
        IReadOnlyList<KnowledgeNode> nodes,
        CancellationToken ct = default)
    {
        var sorted = nodes.OrderByDescending(n => n.Confidence).Take(MaxNodes).ToList();
        if (sorted.Count == 0)
        {
            return new OverviewResult(
                "No knowledge nodes available for this snapshot.",
                "none",
                0,
                DateTimeOffset.UtcNow);
        }

        var provider = providers.Primary;
        if (provider is null)
        {
            return new OverviewResult(
                BuildRuleBasedOverview(sorted),
                "rule-based",
                sorted.Count,
                DateTimeOffset.UtcNow);
        }

        var prompt = BuildPrompt(sorted);
        var promptTokens = (prompt.Length + 3) / 4 + 900;
        if (!budget.TryAllocate(repo.GitHubId, promptTokens, DateTimeOffset.UtcNow))
        {
            return new OverviewResult(
                BuildRuleBasedOverview(sorted),
                "rule-based",
                sorted.Count,
                DateTimeOffset.UtcNow);
        }

        var messages = BuildMessages(prompt);

        try
        {
            var content = await RetryPolicy.WithRetryAsync(
                ct2 => provider.CompleteAsync(messages, ct2),
                _options.MaxRetries,
                ct: ct);
            return new OverviewResult(
                CleanContent(content),
                $"{provider.Name}/{provider.Model}",
                sorted.Count,
                DateTimeOffset.UtcNow);
        }
        catch (Exception) when (providers.Fallback is not null)
        {
            var fallback = providers.Fallback;
            try
            {
                var content = await fallback.CompleteAsync(messages, ct);
                return new OverviewResult(
                    CleanContent(content),
                    $"{fallback.Name}/{fallback.Model}",
                    sorted.Count,
                    DateTimeOffset.UtcNow);
            }
            catch (Exception)
            {
                return new OverviewResult(
                    BuildRuleBasedOverview(sorted),
                    "rule-based",
                    sorted.Count,
                    DateTimeOffset.UtcNow);
            }
        }
        catch (Exception)
        {
            return new OverviewResult(
                BuildRuleBasedOverview(sorted),
                "rule-based",
                sorted.Count,
                DateTimeOffset.UtcNow);
        }
    }

    private static string CleanContent(string content)
    {
        var clean = content.Trim();
        if (clean.StartsWith("```", StringComparison.Ordinal))
        {
            var end = clean.LastIndexOf("```", StringComparison.Ordinal);
            clean = end > 3 ? clean[3..end].Trim() : clean[3..].Trim();
        }
        return clean;
    }

    private static List<ChatMessage> BuildMessages(string prompt) =>
    [
        new("system",
            """
            You are an expert software architect reverse-engineering legacy systems. You receive a
            condensed inventory of the main knowledge nodes extracted from a repository, each with its
            role, bounded context and top responsibility. Produce a concise Markdown project overview
            with these sections:

            ## Summary
            (2-4 sentences: what the system does and its overall architecture, inferred from the nodes)

            ## Main components
            (bulleted list of the most important components, each starting with `[key]` using the node
            key, e.g. `- [src/Order.cs::Order] Order — order management service (Domain)`)

            ## Architectural notes
            (2-4 bullets about layering, patterns and coupling observed across nodes)

            Be concise. Do not invent components that are not in the inventory. Do not wrap the answer
            in code fences.
            """),
        new("user", prompt)
    ];

    private static string BuildPrompt(IReadOnlyList<KnowledgeNode> nodes)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Knowledge node inventory (key | symbol | kind | language | path:lines | role | bounded context | top responsibility):");
        sb.AppendLine();
        foreach (var node in nodes)
        {
            var summary = SummarizeNode(node);
            sb.AppendLine(summary);
        }
        return sb.ToString();
    }

    private static string SummarizeNode(KnowledgeNode node)
    {
        var role = ExtractFirst(RoleRegex, node.Content) ?? node.Kind.ToString();
        var context = ExtractFirst(BoundedContextRegex, node.Content) ?? "unknown";
        var responsibility = ExtractResponsibilities(node.Content);
        var lines = node.EndLine > node.StartLine ? $"{node.StartLine}-{node.EndLine}" : node.StartLine.ToString();
        return $"- [{node.Key}] {node.Symbol} ({node.Kind} | {node.Language}) {node.Path}:{lines} — role: {role}; context: {context}; resp: {responsibility}";
    }

    private static string? ExtractFirst(Regex regex, string content)
    {
        var match = regex.Match(content);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static string ExtractResponsibilities(string content)
    {
        var first = ResponsibilityRegex.Matches(content).FirstOrDefault(m => m.Groups[1].Value.Trim().Length > 3);
        if (first is not null)
        {
            var text = first.Groups[1].Value.Trim();
            return text.Length > 120 ? text[..120] + "…" : text;
        }

        var plain = CodeFenceRegex.Replace(content, " ").Trim();
        return plain.Length > 120 ? plain[..120] + "…" : plain;
    }

    private static string BuildRuleBasedOverview(IReadOnlyList<KnowledgeNode> nodes)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Summary");
        sb.AppendLine("Semantic overview unavailable (no AI provider/budget). Extracted knowledge node inventory:");
        sb.AppendLine();
        sb.AppendLine("## Main components");
        foreach (var node in nodes)
        {
            var role = ExtractFirst(RoleRegex, node.Content) ?? node.Kind.ToString();
            sb.AppendLine($"- [{node.Key}] {node.Symbol} ({node.Kind} | {node.Language}) {node.Path}:{node.StartLine} — {role}");
        }
        return sb.ToString();
    }
}
