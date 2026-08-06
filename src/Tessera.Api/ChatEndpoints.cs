using System.Text.Json;
using Tessera.Infrastructure.Chat;
using Tessera.Infrastructure.Queries;

namespace Tessera.Api;

public static class ChatEndpoints
{
    public static void MapChatEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/repositories/{repositoryId:guid}/chat", async (
            Guid repositoryId,
            ChatRequest request,
            IArchitectureChatService chat,
            CancellationToken ct) =>
        {
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
            IArchitectureChatService chat,
            HttpResponse response,
            CancellationToken ct) =>
        {
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
                await writer.WriteAsync("event: done\ndata: {}\n\n");
                await writer.FlushAsync(ct);
            }
            catch (SnapshotNotFoundException ex)
            {
                await writer.WriteAsync($"event: error\ndata: {JsonSerializer.Serialize(new { error = ex.Message })}\n\n");
                await writer.FlushAsync(ct);
            }

            return Results.Empty;
        });
    }

    private static string Sse(ChatStreamItem item)
    {
        var payload = item.Kind switch
        {
            ChatStreamKind.Mode => JsonSerializer.Serialize(new { mode = item.Mode.ToString() }),
            ChatStreamKind.Warnings => JsonSerializer.Serialize(item.Warnings),
            ChatStreamKind.Delta => JsonSerializer.Serialize(new { text = item.Text }),
            _ => JsonSerializer.Serialize(item.Citations)
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
