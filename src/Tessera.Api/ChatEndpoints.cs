using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tessera.Domain.Entities;
using Tessera.Infrastructure.Chat;
using Tessera.Infrastructure.Data;
using Tessera.Infrastructure.Queries;

namespace Tessera.Api;

public static class ChatEndpoints
{
    public static void MapChatEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/repositories/{repositoryId:guid}/chat/messages", async (
            Guid repositoryId,
            HttpContext context,
            TesseraDbContext db,
            CancellationToken ct) =>
        {
            var guarded = await context.GuardRepoAsync(db, repositoryId, ct);
            if (guarded is not null) return guarded;

            var messages = await db.ConversationMessages.AsNoTracking()
                .Where(m => m.RepositoryId == repositoryId)
                .OrderBy(m => m.CreatedAt)
                .Take(200)
                .ToListAsync(ct);

            return Results.Ok(messages.Select(MessageDto.From));
        });

        app.MapPost("/api/repositories/{repositoryId:guid}/chat/messages", async (
            Guid repositoryId,
            ChatHistoryEntry entry,
            HttpContext context,
            TesseraDbContext db,
            CancellationToken ct) =>
        {
            var guarded = await context.GuardRepoAsync(db, repositoryId, ct);
            if (guarded is not null) return guarded;

            var role = (entry.Role ?? "").Trim().ToLowerInvariant();
            if (role is not ("user" or "assistant"))
            {
                return Results.BadRequest(new { error = "role must be 'user' or 'assistant'" });
            }
            if (string.IsNullOrWhiteSpace(entry.Content))
            {
                return Results.BadRequest(new { error = "content is required" });
            }

            var message = new ConversationMessage
            {
                Id = Guid.NewGuid(),
                RepositoryId = repositoryId,
                Role = role,
                Content = entry.Content,
                Mode = string.IsNullOrWhiteSpace(entry.Mode) ? null : entry.Mode,
                CitationsJson = JsonSerializer.Serialize(entry.Citations ?? Array.Empty<CitationEntry>()),
                WarningsJson = JsonSerializer.Serialize(entry.Warnings ?? Array.Empty<string>()),
                CreatedAt = DateTimeOffset.UtcNow
            };
            db.ConversationMessages.Add(message);
            await db.SaveChangesAsync(ct);

            return Results.Ok(MessageDto.From(message));
        });

        app.MapPost("/api/repositories/{repositoryId:guid}/chat", async (
            Guid repositoryId,
            ChatRequest request,
            HttpContext context,
            TesseraDbContext db,
            IArchitectureChatService chat,
            CancellationToken ct) =>
        {
            var guarded = await context.GuardRepoAsync(db, repositoryId, ct);
            if (guarded is not null) return guarded;
            if (string.IsNullOrWhiteSpace(request.Question))
            {
                return Results.BadRequest(new { error = "question is required" });
            }
            try
            {
                var result = await chat.AnswerAsync(
                    repositoryId,
                    request.Question,
                    request.Commit,
                    request.TopK,
                    request.Threshold,
                    ct);
                return Results.Ok(result);
            }
            catch (SnapshotNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        });

        app.MapPost("/api/repositories/{repositoryId:guid}/chat/stream", async (
            Guid repositoryId,
            ChatRequest request,
            HttpContext context,
            TesseraDbContext db,
            IArchitectureChatService chat,
            HttpResponse response,
            CancellationToken ct) =>
        {
            var guarded = await context.GuardRepoAsync(db, repositoryId, ct);
            if (guarded is not null) return guarded;
            if (string.IsNullOrWhiteSpace(request.Question))
            {
                return Results.BadRequest(new { error = "question is required" });
            }

            response.ContentType = "text/event-stream";
            response.Headers.CacheControl = "no-cache";
            var writer = new StreamWriter(response.Body);

            try
            {
                await foreach (var item in chat.AnswerStreamAsync(
                    repositoryId,
                    request.Question,
                    request.Commit,
                    request.TopK,
                    request.Threshold,
                    ct))
                {
                    await writer.WriteAsync(Sse(item));
                    await writer.FlushAsync(ct);
                }
            }
            catch (SnapshotNotFoundException ex)
            {
                await writer.WriteAsync($"event: error\ndata: {JsonSerializer.Serialize(new { error = ex.Message })}\n\n");
                await writer.FlushAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                await writer.WriteAsync($"event: error\ndata: {JsonSerializer.Serialize(new { error = ex.Message })}\n\n");
                await writer.FlushAsync(ct);
            }
            finally
            {
                await writer.WriteAsync("event: done\ndata: {}\n\n");
                await writer.FlushAsync(CancellationToken.None);
            }

            return Results.Empty;
        });
    }

    // Web defaults (camelCase) match what the dashboard expects; the history endpoints get this
    // automatically from minimal API binding, manual SSE serialization must opt in.
    private static readonly JsonSerializerOptions SseJsonOptions = new(JsonSerializerDefaults.Web);

    private static string Sse(ChatStreamItem item)
    {
        var payload = item.Kind switch
        {
            ChatStreamKind.Mode => JsonSerializer.Serialize(new { mode = item.Mode.ToString() }, SseJsonOptions),
            ChatStreamKind.Warnings => JsonSerializer.Serialize(item.Warnings, SseJsonOptions),
            ChatStreamKind.Delta => JsonSerializer.Serialize(new { text = item.Text }, SseJsonOptions),
            _ => JsonSerializer.Serialize(item.Citations, SseJsonOptions)
        };
        var name = item.Kind switch
        {
            ChatStreamKind.Mode => "mode",
            ChatStreamKind.Warnings => "warnings",
            ChatStreamKind.Delta => "delta",
            _ => "citations"
        };
        return $"event: {name}\ndata: {payload}\n\n";
    }
}

public sealed class ChatRequest
{
    public string Question { get; set; } = "";
    public string? Commit { get; set; }
    public int? TopK { get; set; }
    public double? Threshold { get; set; }
}

public sealed class CitationEntry
{
    public string Key { get; set; } = "";
    public string Symbol { get; set; } = "";
    public string File { get; set; } = "";
    public int Line { get; set; }
    public string Label { get; set; } = "";
}

public sealed class ChatHistoryEntry
{
    public string Role { get; set; } = "";
    public string Content { get; set; } = "";
    public string? Mode { get; set; }
    public CitationEntry[]? Citations { get; set; }
    public string[]? Warnings { get; set; }
}

public sealed class MessageDto
{
    public Guid Id { get; set; }
    public string Role { get; set; } = "";
    public string Content { get; set; } = "";
    public string? Mode { get; set; }
    public List<CitationEntry> Citations { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; }

    public static MessageDto From(ConversationMessage message) => new()
    {
        Id = message.Id,
        Role = message.Role,
        Content = message.Content,
        Mode = message.Mode,
        Citations = DeserializeCitations(message.CitationsJson),
        Warnings = DeserializeWarnings(message.WarningsJson),
        CreatedAt = message.CreatedAt
    };

    private static List<CitationEntry> DeserializeCitations(string json) =>
        JsonSerializer.Deserialize<List<CitationEntry>>(json) ?? new List<CitationEntry>();

    private static List<string> DeserializeWarnings(string json) =>
        JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
}
