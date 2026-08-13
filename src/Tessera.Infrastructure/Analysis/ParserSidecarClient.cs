using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using Tessera.Domain.Parsing;

namespace Tessera.Infrastructure.Analysis;

public sealed class ParserSidecarOptions
{
    public string BaseUrl { get; set; } = "http://localhost:4350";
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(5);
}

public interface IParserSidecarClient
{
    Task<ParseResult> ParseAsync(string commitSha, string defaultBranch, IReadOnlyList<ParsedSourceFile> files, CancellationToken ct = default);
}

public sealed record ParsedSourceFile(string Path, string Content, string? Language = null);

public sealed class ParserSidecarClient : IParserSidecarClient
{
    private readonly HttpClient _http;

    public ParserSidecarClient(IHttpClientFactory factory, IOptions<ParserSidecarOptions> options)
    {
        _http = factory.CreateClient(nameof(ParserSidecarClient));
        _http.BaseAddress = new Uri(options.Value.BaseUrl.TrimEnd('/') + "/");
        _http.Timeout = options.Value.Timeout;
    }

    public async Task<ParseResult> ParseAsync(string commitSha, string defaultBranch, IReadOnlyList<ParsedSourceFile> files, CancellationToken ct = default)
    {
        if (files.Count == 0)
        {
            return new ParseResult { CommitSha = commitSha, DefaultBranch = defaultBranch };
        }

        var payload = new
        {
            commitSha,
            defaultBranch,
            files = files.Select(f => new { path = f.Path, content = f.Content, language = f.Language })
        };

        var response = await _http.PostAsJsonAsync("parse", payload, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw await ParseFailureException(response, ct);
        }
        var result = await response.Content.ReadFromJsonAsync<SidecarParseResponse>(ct);
        if (result is null)
        {
            throw new InvalidOperationException("Sidecar returned empty response.");
        }

        return new ParseResult
        {
            CommitSha = result.CommitSha ?? commitSha,
            DefaultBranch = result.DefaultBranch ?? defaultBranch,
            Entities = result.Entities.Select(e => new ParsedEntity
            {
                Key = e.Key,
                Path = e.Path,
                Symbol = e.Symbol,
                Kind = Enum.TryParse<Tessera.Domain.Enums.NodeKind>(e.Kind, true, out var kind) ? kind : Tessera.Domain.Enums.NodeKind.Class,
                Language = e.Language,
                StartLine = e.StartLine,
                EndLine = e.EndLine,
                Source = e.Source,
                StructuralHash = e.StructuralHash
            }).ToList(),
            Relationships = result.Relationships.Select(r => new ParsedRelationship
            {
                From = r.From,
                To = r.To,
                Type = Enum.TryParse<Tessera.Domain.Enums.EdgeType>(r.Type, true, out var edgeType) ? edgeType : Tessera.Domain.Enums.EdgeType.References,
                Evidence = r.Evidence,
                Confidence = r.Confidence,
                IsStatic = r.IsStatic
            }).ToList(),
            Diagnostics = result.Diagnostics ?? new List<string>()
        };
    }

    private static async Task<HttpRequestException> ParseFailureException(HttpResponseMessage response, CancellationToken ct)
    {
        string detail = string.Empty;
        try
        {
            var body = await response.Content.ReadFromJsonAsync<SidecarErrorResponse>(ct);
            if (body is not null)
            {
                var failures = body.Failures is { Count: > 0 }
                    ? string.Join("; ", body.Failures.Select(f => $"{f.Path}: {f.Message}"))
                    : body.Error;
                detail = failures ?? body.Error ?? "";
            }
        }
        catch
        {
            // Reading the error body is best-effort; fall back to the status code.
        }

        var message = string.IsNullOrWhiteSpace(detail)
            ? $"Parser sidecar returned {(int)response.StatusCode} {response.StatusCode}."
            : $"Parser sidecar returned {(int)response.StatusCode} {response.StatusCode}: {detail}";
        return new HttpRequestException(message);
    }

    private sealed class SidecarErrorResponse
    {
        public string? Error { get; set; }
        public List<SidecarFailure>? Failures { get; set; }
    }

    private sealed class SidecarFailure
    {
        public string Path { get; set; } = "";
        public string Message { get; set; } = "";
    }

    private sealed class SidecarParseResponse
    {
        public string? CommitSha { get; set; }
        public string? DefaultBranch { get; set; }
        public List<SidecarEntity> Entities { get; set; } = new();
        public List<SidecarRelationship> Relationships { get; set; } = new();
        public List<string>? Diagnostics { get; set; }
    }

    private sealed class SidecarEntity
    {
        public string Key { get; set; } = "";
        public string Path { get; set; } = "";
        public string Symbol { get; set; } = "";
        public string Kind { get; set; } = "";
        public string Language { get; set; } = "";
        public int StartLine { get; set; }
        public int EndLine { get; set; }
        public string? Source { get; set; }
        public string StructuralHash { get; set; } = "";
    }

    private sealed class SidecarRelationship
    {
        public string From { get; set; } = "";
        public string To { get; set; } = "";
        public string Type { get; set; } = "";
        public string? Evidence { get; set; }
        public double Confidence { get; set; } = 1.0;
        public bool IsStatic { get; set; } = true;
    }
}
