using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tessera.Domain.Merkle;
using Tessera.Domain.Parsing;
using Tessera.Domain.Ports;

namespace Tessera.Infrastructure.Ai;

public sealed class AiSummarizer : ISemanticSummarizer
{
    public const string PromptVersionConst = "1.1.0";

    private static readonly Regex ConfidenceRegex = new(
        @"(?im)^\s*confidence\s*[:=-]?\s*([0-9]+(?:\.[0-9]+)?)\s*$",
        RegexOptions.Compiled);

    private readonly IProviderRegistry _providers;
    private readonly RuleBasedSummarizer _ruleBased;
    private readonly TokenBudgetTracker _budget;
    private readonly AiOptions _options;
    private readonly ILogger<AiSummarizer> _log;
    private readonly object _throttleLock = new();
    private long _lastCallTicks = Environment.TickCount64;

    public AiSummarizer(
        IProviderRegistry providers,
        RuleBasedSummarizer ruleBased,
        TokenBudgetTracker budget,
        IOptions<AiOptions> options,
        ILogger<AiSummarizer> log)
    {
        _providers = providers;
        _ruleBased = ruleBased;
        _budget = budget;
        _options = options.Value;
        _log = log;
    }

    public string PromptVersion => PromptVersionConst;

    public async Task<AiContent> SummarizeAsync(
        ParsedEntity entity,
        IReadOnlyList<ParsedRelationship> relationships,
        long repositoryId,
        CancellationToken ct = default)
    {
        var primary = SelectProvider(entity);
        if (primary is null)
        {
            return await _ruleBased.SummarizeAsync(entity, relationships, repositoryId, ct);
        }

        var prompt = BuildPrompt(entity, relationships);
        var promptTokens = EstimateTokens(prompt) + 600;
        if (!_budget.TryAllocate(repositoryId, promptTokens, DateTimeOffset.UtcNow))
        {
            return await _ruleBased.SummarizeAsync(entity, relationships, repositoryId, ct);
        }

        var messages = BuildMessages(prompt);

        try
        {
            await ThrottleAsync(ct);
            var content = await RetryPolicy.WithRetryAsync(ct2 => primary.CompleteAsync(messages, ct2), _options.MaxRetries, ct: ct);
            return ParseResponse(content, primary);
        }
        catch (Exception ex) when (_providers.Fallback is not null)
        {
            _log.LogWarning(ex, "Primary provider {provider} failed for {entity}, falling back", primary.Name, entity.Key);
            var fallback = _providers.Fallback;
            try
            {
                await ThrottleAsync(ct);
                var content = await fallback.CompleteAsync(messages, ct);
                return ParseResponse(content, fallback);
            }
            catch (Exception ex2)
            {
                _log.LogError(ex2, "Fallback provider {provider} failed for {entity}, using rule-based", fallback.Name, entity.Key);
                return await _ruleBased.SummarizeAsync(entity, relationships, repositoryId, ct);
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Provider {provider} failed for {entity}, using rule-based", primary.Name, entity.Key);
            return await _ruleBased.SummarizeAsync(entity, relationships, repositoryId, ct);
        }
    }

    private IChatProvider? SelectProvider(ParsedEntity entity)
    {
        var complexity = entity.EndLine - entity.StartLine;
        if (complexity >= _options.ComplexityThresholdLines)
        {
            return _providers.LargeTier ?? _providers.Primary;
        }
        return _providers.Primary;
    }

    private static List<ChatMessage> BuildMessages(string prompt) =>
    [
        new("system",
            """
            You are an expert software architect reverse-engineering legacy systems.
            You receive a JSON description of a source entity (class/function/module) and its
            known static relationships. Produce a Markdown knowledge node ONLY, with these sections:

            ## Type
            ## Responsibilities
            (concise bullet list inferred from structure and naming)
            ## Dependencies
            (bullet list of the entities this entity depends on; add one line "None" if empty)
            ## Incoming references
            (bullet list of consumers; add one line "None" if empty)
            ## Events
            (bullet list of events published/consumed; add one line "None" if none are evident)
            ## Architecture
            (two bullets: `- Bounded context: <name>` inferred from the codebase layout, and
            `- Role: <role>` such as Controller, Service, Repository, Domain, Contract, DTO,
            EventPublisher, or Configuration)
            ## Confidence

            End the node with a single line `Confidence: 0.xx` where xx is 0.00-1.00 based on how
            certain you are of your semantic inferences (structural facts score high, guesses low).
            Do not include any text outside the Markdown node. Do not wrap it in code fences.
            """),
        new("user", prompt)
    ];

    private static string BuildPrompt(ParsedEntity entity, IReadOnlyList<ParsedRelationship> relationships)
    {
        var dependencies = relationships.Where(r => r.From == entity.Key).Select(r => r.To).Distinct().ToList();
        var consumers = relationships.Where(r => r.To == entity.Key).Select(r => r.From).Distinct().ToList();

        return JsonSerializer.Serialize(new
        {
            entity = new
            {
                key = entity.Key,
                symbol = entity.Symbol,
                kind = entity.Kind.ToString(),
                language = entity.Language,
                path = entity.Path,
                lines = new { entity.StartLine, entity.EndLine }
            },
            dependencies,
            consumers
        });
    }

    private static AiContent ParseResponse(string content, IChatProvider provider)
    {
        var clean = content.Trim();
        if (clean.StartsWith("```", StringComparison.Ordinal))
        {
            var end = clean.LastIndexOf("```", StringComparison.Ordinal);
            clean = end > 3 ? clean[3..end].Trim() : clean[3..].Trim();
        }

        var confidence = 0.7;
        var match = ConfidenceRegex.Match(clean);
        if (match.Success && double.TryParse(match.Groups[1].Value, out var parsed))
        {
            confidence = Math.Clamp(parsed, 0, 1);
        }

        if (string.IsNullOrWhiteSpace(clean))
        {
            throw new ChatProviderException($"Provider '{provider.Name}' returned empty content.");
        }

        return new AiContent
        {
            Content = clean,
            Confidence = confidence,
            Model = $"{provider.Name}/{provider.Model}",
            PromptVersion = PromptVersionConst
        };
    }

    private static long EstimateTokens(string text) => (text.Length + 3) / 4;

    private async Task ThrottleAsync(CancellationToken ct)
    {
        var rpm = _options.RequestsPerMinute;
        if (rpm <= 0)
        {
            return;
        }

        var minIntervalMs = 60000.0 / rpm;
        long delayMs;
        lock (_throttleLock)
        {
            var now = Environment.TickCount64;
            var next = Math.Max(_lastCallTicks + (long)minIntervalMs, now);
            _lastCallTicks = next;
            delayMs = next - now;
        }

        if (delayMs > 0)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(delayMs), ct);
        }
    }
}
