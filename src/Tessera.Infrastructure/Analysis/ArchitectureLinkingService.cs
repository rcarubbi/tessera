using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Tessera.Domain.Enums;
using Tessera.Domain.Parsing;
using Tessera.Domain.Ports;
using Tessera.Infrastructure.Ai;

namespace Tessera.Infrastructure.Analysis;

public sealed record LinkedEdge(string From, string To, EdgeType Type, string? Evidence, double Confidence);

public interface IArchitectureLinkingService
{
    Task<IReadOnlyList<LinkedEdge>> LinkAsync(ParseResult parse, long repositoryId, CancellationToken ct = default);
}

public sealed class ArchitectureLinkingService(
    IProviderRegistry providers,
    TokenBudgetTracker budget,
    IOptions<AiOptions> options) : IArchitectureLinkingService
{
    private const int MaxEntities = 120;
    private const string SystemPrompt =
        """
        You are a software architect auditing a mixed-technology repository. Static analysis already
        captured connections inside each technology. Your job is to find MISSING cross-technology
        connections a human architect would recognize from names, paths and route patterns, for
        example a TypeScript service that fetches '/api/orders' talking to a C# OrdersController.
        Rules:
        - Use type "InvokesEndpoint" when a client calls an HTTP endpoint (fetch/axios/http/XMLHttpRequest).
        - Use type "Calls" for a logical cross-technology invocation that is not HTTP.
        - Only propose edges you are confident about. Do not invent entities.
        Return ONLY a JSON array of edge objects with fields: from, to, type, evidence, confidence.
        No markdown, no explanations.
        """;

    private readonly AiOptions _options = options.Value;

    public async Task<IReadOnlyList<LinkedEdge>> LinkAsync(ParseResult parse, long repositoryId, CancellationToken ct = default)
    {
        if (parse.Entities.Count == 0)
        {
            return Array.Empty<LinkedEdge>();
        }

        var languageCount = parse.Entities.Select(e => e.Language).Distinct().Count();
        if (languageCount <= 1)
        {
            return Array.Empty<LinkedEdge>();
        }

        var provider = providers.Primary;
        if (provider is null)
        {
            return Array.Empty<LinkedEdge>();
        }

        var selected = SelectCandidates(parse);
        if (selected.Count == 0)
        {
            return Array.Empty<LinkedEdge>();
        }

        var prompt = BuildPrompt(selected);
        var promptTokens = (prompt.Length + 3) / 4 + 500;
        if (!budget.TryAllocate(repositoryId, promptTokens, DateTimeOffset.UtcNow))
        {
            return Array.Empty<LinkedEdge>();
        }

        var messages = new[] { new ChatMessage("system", SystemPrompt), new ChatMessage("user", prompt) };

        string? content = null;
        try
        {
            content = await RetryPolicy.WithRetryAsync(ct2 => provider.CompleteAsync(messages, ct2), _options.MaxRetries, ct: ct);
        }
        catch (Exception) when (providers.Fallback is not null)
        {
            try
            {
                content = await providers.Fallback.CompleteAsync(messages, ct);
            }
            catch (Exception)
            {
            }
        }
        catch (Exception)
        {
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            return Array.Empty<LinkedEdge>();
        }

        return ParseEdges(content, parse);
    }

    private static List<ParsedEntity> SelectCandidates(ParseResult parse)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var selected = new List<ParsedEntity>();

        var endpointish = parse.Entities
            .Where(IsEndpointish)
            .OrderByDescending(e => e.EndLine - e.StartLine)
            .Take(MaxEntities / 2);
        foreach (var entity in endpointish)
        {
            if (seen.Add(entity.Key))
            {
                selected.Add(entity);
            }
        }

        var rest = parse.Entities
            .Where(e => !IsEndpointish(e))
            .OrderByDescending(e => e.EndLine - e.StartLine)
            .Take(MaxEntities / 2);
        foreach (var entity in rest)
        {
            if (seen.Add(entity.Key))
            {
                selected.Add(entity);
            }
        }

        return selected;
    }

    private static bool IsEndpointish(ParsedEntity entity)
    {
        var hay = $"{entity.Symbol} {entity.Path}".ToLowerInvariant();
        return hay.Contains("controller")
            || hay.Contains("endpoint")
            || hay.Contains("handler")
            || hay.Contains("route")
            || hay.Contains("/api/")
            || hay.Contains("\\api\\")
            || hay.Contains("http")
            || hay.Contains("fetch")
            || hay.Contains("axios")
            || hay.Contains("client")
            || hay.Contains("service");
    }

    private static string BuildPrompt(IReadOnlyList<ParsedEntity> entities)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Entity inventory (key | symbol | kind | language | path:lines):");
        foreach (var entity in entities)
        {
            sb.AppendLine($"- [{entity.Key}] {entity.Symbol} ({entity.Kind} | {entity.Language}) {entity.Path}:{entity.StartLine}-{entity.EndLine}");
        }
        return sb.ToString();
    }

    private static IReadOnlyList<LinkedEdge> ParseEdges(string content, ParseResult parse)
    {
        var keys = parse.Entities.Select(e => e.Key).ToHashSet(StringComparer.Ordinal);
        var existing = parse.Relationships
            .Select(r => $"{r.From}|{r.To}|{r.Type}")
            .ToHashSet(StringComparer.Ordinal);

        var clean = content.Trim();
        if (clean.StartsWith("```", StringComparison.Ordinal))
        {
            var end = clean.LastIndexOf("```", StringComparison.Ordinal);
            clean = end > 3 ? clean[3..end].Trim() : clean[3..].Trim();
        }

        List<LinkedEdge> edges;
        try
        {
            using var doc = JsonDocument.Parse(clean);
            edges = doc.RootElement.ValueKind == JsonValueKind.Array
                ? doc.RootElement.EnumerateArray()
                    .Select(ReadEdge)
                    .Where(e => e is not null)
                    .Select(e => e!)
                    .ToList()
                : new List<LinkedEdge>();
        }
        catch (JsonException)
        {
            return Array.Empty<LinkedEdge>();
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<LinkedEdge>();
        foreach (var edge in edges)
        {
            if (!keys.Contains(edge.From) || !keys.Contains(edge.To) || edge.From == edge.To)
            {
                continue;
            }
            if (edge.Type is not (EdgeType.Calls or EdgeType.InvokesEndpoint))
            {
                continue;
            }
            var signature = $"{edge.From}|{edge.To}|{edge.Type}";
            if (existing.Contains(signature) || !seen.Add(signature))
            {
                continue;
            }
            result.Add(edge);
        }
        return result;
    }

    private static LinkedEdge? ReadEdge(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        var from = ReadString(element, "from");
        var to = ReadString(element, "to");
        var type = ReadString(element, "type");
        if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to) || string.IsNullOrWhiteSpace(type))
        {
            return null;
        }
        if (!Enum.TryParse<EdgeType>(type, true, out var edgeType))
        {
            return null;
        }
        var evidence = ReadString(element, "evidence");
        var confidence = 0.7;
        if (element.TryGetProperty("confidence", out var c))
        {
            confidence = c.ValueKind == JsonValueKind.Number && c.TryGetDouble(out var cd) ? Math.Clamp(cd, 0, 1) : confidence;
        }
        return new LinkedEdge(from, to, edgeType, evidence, confidence);
    }

    private static string? ReadString(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }
        var text = value.GetString()?.Trim();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }
}
