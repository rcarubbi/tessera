using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text.RegularExpressions;
using Tessera.Domain.Entities;
using Tessera.Domain.Enums;
using Tessera.Domain.Ports;
using Tessera.Infrastructure.Ai;
using Tessera.Infrastructure.Chat;
using Tessera.Infrastructure.Data;
using Tessera.Infrastructure.Queries;

namespace Tessera.Integration.Tests;

public sealed class ArchitectureChatTests
{
    [Fact]
    public async Task Structural_question_answers_from_graph_without_llm()
    {
        using var db = ChatSeed.CreateDb();
        await ChatSeed.SeedAsync(db, ChatSeed.OrderGraphSnapshot());

        var chat = ChatSeed.CreateChat(db, primary: null, fallback: null, embedding: null);
        var result = await chat.AnswerAsync(ChatSeed.RepoId, "what breaks if I change Order?");

        Assert.Equal(ChatMode.Graph, result.Mode);
        Assert.Contains("OrderService", result.Answer);
        Assert.Contains(result.Citations, c => c.Symbol == "OrderService" && c.Line > 0);
    }

    [Fact]
    public async Task Consumer_question_lists_reverse_edges()
    {
        using var db = ChatSeed.CreateDb();
        await ChatSeed.SeedAsync(db, ChatSeed.OrderGraphSnapshot());

        var chat = ChatSeed.CreateChat(db, primary: null, fallback: null, embedding: null);
        var result = await chat.AnswerAsync(ChatSeed.RepoId, "who uses Order?");

        Assert.Equal(ChatMode.Graph, result.Mode);
        Assert.Contains("referenced by", result.Answer);
        Assert.Contains(result.Citations, c => c.Symbol == "OrderService");
    }

    [Fact]
    public async Task Semantic_question_uses_rag_with_lexical_retrieval()
    {
        using var db = ChatSeed.CreateDb();
        await ChatSeed.SeedAsync(db, ChatSeed.OrderGraphSnapshot());

        var chat = ChatSeed.CreateChat(db, new FakeChat("primary", "chat-model", _ => "The Order entity handles orders. See [Order.cs::Order]."), null, null);
        var result = await chat.AnswerAsync(ChatSeed.RepoId, "why does Order exist?");

        Assert.Equal(ChatMode.Rag, result.Mode);
        Assert.Contains("The Order entity handles orders", result.Answer);
        Assert.Contains(result.Citations, c => c.Key == "Order.cs::Order");
    }

    [Fact]
    public async Task Returns_no_context_when_nothing_matches()
    {
        using var db = ChatSeed.CreateDb();
        await ChatSeed.SeedAsync(db, ChatSeed.OrderGraphSnapshot());

        var chat = ChatSeed.CreateChat(db, primary: null, fallback: null, embedding: null);
        var result = await chat.AnswerAsync(ChatSeed.RepoId, "quantum entanglement frobnicate the widget?");

        Assert.Equal(ChatMode.NoContext, result.Mode);
        Assert.Contains("couldn't find relevant context", result.Answer);
    }

    [Fact]
    public async Task Rags_warns_on_low_confidence_node()
    {
        using var db = ChatSeed.CreateDb();
        await ChatSeed.SeedAsync(db, ChatSeed.OrderGraphSnapshot(lowConfidence: true));

        var chat = ChatSeed.CreateChat(db, new FakeChat("primary", "chat-model", _ => "Order is important. [Order.cs::Order]"), null, null);
        var result = await chat.AnswerAsync(ChatSeed.RepoId, "what does Order do?");

        Assert.Equal(ChatMode.Rag, result.Mode);
        Assert.NotEmpty(result.Warnings);
        Assert.Contains("needs review", result.Warnings[0]);
    }

    [Fact]
    public async Task Retrieval_is_scoped_to_the_requested_snapshot()
    {
        using var db = ChatSeed.CreateDb();
        await ChatSeed.SeedAsync(db, ChatSeed.OrderGraphSnapshot());
        await ChatSeed.SeedAsync(db, ChatSeed.AuditGraphSnapshot());

        var retrieval = new RetrievalService(db, ChatSeed.EmptyRegistry());
        var current = await retrieval.RetrieveAsync(ChatSeed.RepoId, null, "audit log", 5, 0.0);
        var historical = await retrieval.RetrieveAsync(ChatSeed.RepoId, ChatSeed.Sha1, "audit log", 5, 0.0);

        Assert.Contains(current, r => r.Node.Symbol == "Audit");
        Assert.DoesNotContain(historical, r => r.Node.Symbol == "Audit");
    }

    [Fact]
    public async Task Embedding_retrieval_scores_by_cosine_and_caches()
    {
        using var db = ChatSeed.CreateDb();
        await ChatSeed.SeedAsync(db, ChatSeed.OrderGraphSnapshot());

        var embedding = new FakeEmbedding(ChatSeed.BoxEmbeddingHandler);
        var retrieval = new RetrievalService(db, ChatSeed.Registry(embedding: embedding));
        var results = await retrieval.RetrieveAsync(ChatSeed.RepoId, null, "how does order work?", 5, 0.5);

        Assert.Equal("Order", results[0].Node.Symbol);
        var embeddings = await db.NodeEmbeddings.ToListAsync();
        Assert.Equal(2, embeddings.Count);
        Assert.All(embeddings, e => Assert.Equal(3, e.Dimensions));
    }

    [Fact]
    public async Task Falls_back_to_node_synthesis_when_budget_exhausted()
    {
        using var db = ChatSeed.CreateDb();
        await ChatSeed.SeedAsync(db, ChatSeed.OrderGraphSnapshot());

        var primary = new FakeChat("primary", "chat-model", _ => "unused");
        var chat = ChatSeed.CreateChat(db, primary, null, null, dailyBudget: 0);
        var result = await chat.AnswerAsync(ChatSeed.RepoId, "how does order work?");

        Assert.Equal(ChatMode.Rag, result.Mode);
        Assert.Contains("Top relevant context", result.Answer);
        Assert.Equal(0, primary.Calls);
    }

    [Fact]
    public async Task Unknown_repository_returns_404()
    {
        using var db = ChatSeed.CreateDb();
        var chat = ChatSeed.CreateChat(db, primary: null, fallback: null, embedding: null);

        await Assert.ThrowsAsync<SnapshotNotFoundException>(() =>
            chat.AnswerAsync(Guid.NewGuid(), "what breaks if I change Order?"));
    }

    [Fact]
    public async Task Stream_emits_mode_deltas_and_citations()
    {
        using var db = ChatSeed.CreateDb();
        await ChatSeed.SeedAsync(db, ChatSeed.OrderGraphSnapshot());

        var chat = ChatSeed.CreateChat(db, primary: null, fallback: null, embedding: null);
        var items = new List<ChatStreamItem>();
        await foreach (var item in chat.AnswerStreamAsync(ChatSeed.RepoId, "what breaks if I change Order?"))
        {
            items.Add(item);
        }

        Assert.Equal(ChatStreamKind.Mode, items[0].Kind);
        Assert.Equal(ChatMode.Graph, items[0].Mode);
        var deltas = items.Where(i => i.Kind == ChatStreamKind.Delta).Select(i => i.Text).ToList();
        Assert.NotEmpty(deltas);
        Assert.All(deltas, d => Assert.False(string.IsNullOrEmpty(d)));
        Assert.True(string.Join("", deltas).Length > 20, $"answer={string.Join("", deltas)}");
        var citations = items.Single(i => i.Kind == ChatStreamKind.Citations).Citations!;
        Assert.Contains(citations, c => c.Symbol == "OrderService" && c.Line > 0);
    }

    [Fact]
    public async Task Stream_no_context_emits_mode_and_deltas()
    {
        using var db = ChatSeed.CreateDb();
        await ChatSeed.SeedAsync(db, ChatSeed.OrderGraphSnapshot());

        var chat = ChatSeed.CreateChat(db, primary: null, fallback: null, embedding: null);
        var items = new List<ChatStreamItem>();
        await foreach (var item in chat.AnswerStreamAsync(ChatSeed.RepoId, "quantum entanglement frobnicate?"))
        {
            items.Add(item);
        }

        Assert.Equal(ChatMode.NoContext, items[0].Mode);
        Assert.Contains(items, i => i.Kind == ChatStreamKind.Delta);
    }
}

internal sealed class ChatSeed
{
    public static readonly Guid RepoId = Guid.NewGuid();
    public const string Sha1 = "c08a271341e9277c4733cc2d7f3d04cec34f853b";
    public const string Sha2 = "d21a7ba5d482e9efe41fc42ce10284a26b20d631";

    public static TesseraDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<TesseraDbContext>()
            .UseInMemoryDatabase($"chat-{Guid.NewGuid():N}")
            .EnableSensitiveDataLogging()
            .Options);

    public static IProviderRegistry EmptyRegistry() => new FakeRegistry();

    public static IProviderRegistry Registry(
        FakeChat? primary = null,
        FakeChat? fallback = null,
        FakeEmbedding? embedding = null) => new FakeRegistry(primary, fallback, embedding);

    public static ArchitectureChatService CreateChat(
        TesseraDbContext db,
        FakeChat? primary,
        FakeChat? fallback,
        FakeEmbedding? embedding,
        long dailyBudget = 10_000_000)
    {
        var options = Options.Create(new AiOptions
        {
            ReviewThreshold = 0.7,
            TopK = 5,
            SimilarityThreshold = 0.0,
            DailyBudgetTokens = dailyBudget
        });
        return new ArchitectureChatService(
            db,
            new GraphQueryService(db),
            new RetrievalService(db, Registry(primary, fallback, embedding)),
            Registry(primary, fallback, embedding),
            new TokenBudgetTracker(options),
            options);
    }

    public static async Task SeedAsync(
        TesseraDbContext db,
        (Snapshot Snapshot, List<KnowledgeNode> Nodes, List<GraphEdge> Edges) seed)
    {
        if (!await db.Repositories.AnyAsync(r => r.Id == RepoId))
        {
            db.Repositories.Add(new Repository
            {
                Id = RepoId,
                GitHubId = 42,
                Owner = "e2e",
                Name = "sample",
                FullName = "e2e/sample",
                DefaultBranch = "main",
                CloneUrl = "/repos/e2e-origin",
                InstallationId = 1,
                IsConnected = true,
                Status = ProcessingStatus.Completed,
                NodeCount = seed.Nodes.Count,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }
        db.Snapshots.Add(seed.Snapshot);
        db.KnowledgeNodes.AddRange(seed.Nodes);
        db.GraphEdges.AddRange(seed.Edges);
        await db.SaveChangesAsync();
    }

    private static int _snapshotCounter;

    public static (Snapshot, List<KnowledgeNode>, List<GraphEdge>) OrderGraphSnapshot(bool lowConfidence = false)
    {
        var snapshotId = Guid.NewGuid();
        var order = Node(snapshotId, "Order.cs::Order", "Order", "Order.cs", 12, 40, lowConfidence ? 0.5 : 0.9);
        var orderService = Node(snapshotId, "OrderService.cs::OrderService", "OrderService", "OrderService.cs", 5, 30, 0.85);
        var edge = new GraphEdge
        {
            Id = Guid.NewGuid(),
            RepositoryId = RepoId,
            SnapshotId = snapshotId,
            FromNodeId = orderService.Id,
            FromKey = orderService.Key,
            ToNodeId = order.Id,
            ToKey = order.Key,
            Type = EdgeType.Calls,
            Evidence = "OrderService.cs",
            Confidence = 1.0,
            IsStatic = true
        };
        return (SnapshotFor(snapshotId, Sha1, 2), new List<KnowledgeNode> { order, orderService }, new List<GraphEdge> { edge });
    }

    public static (Snapshot, List<KnowledgeNode>, List<GraphEdge>) AuditGraphSnapshot()
    {
        var snapshotId = Guid.NewGuid();
        var audit = Node(snapshotId, "Audit.cs::Audit", "Audit", "Audit.cs", 8, 22, 0.8);
        var order = Node(snapshotId, "Order.cs::Order", "Order", "Order.cs", 12, 40, 0.9);
        return (SnapshotFor(snapshotId, Sha2, 2), new List<KnowledgeNode> { audit, order }, new List<GraphEdge>());
    }

    public static Func<string, float[]> BoxEmbeddingHandler => text =>
    {
        var words = Regex.Matches(text.ToLowerInvariant(), @"[\p{L}\p{N}_]+")
            .Select(m => m.Value)
            .ToHashSet(StringComparer.Ordinal);
        return new float[]
        {
            words.Contains("order") ? 1f : 0f,
            words.Contains("payment") ? 1f : 0f,
            words.Contains("audit") ? 1f : 0f
        };
    };

    private static Snapshot SnapshotFor(Guid snapshotId, string sha, int nodeCount) => new()
    {
        Id = snapshotId,
        RepositoryId = RepoId,
        CommitSha = sha,
        RootHash = $"root-{sha}",
        NodeCount = nodeCount,
        EdgeCount = 0,
        Status = ProcessingStatus.Completed,
        CreatedAt = DateTimeOffset.UtcNow.AddSeconds(Interlocked.Increment(ref _snapshotCounter))
    };

    private static KnowledgeNode Node(Guid snapshotId, string key, string symbol, string path, int start, int end, double confidence) => new()
    {
        Id = Guid.NewGuid(),
        RepositoryId = RepoId,
        SnapshotId = snapshotId,
        Key = key,
        Path = path,
        Symbol = symbol,
        Kind = NodeKind.Class,
        Language = "csharp",
        StartLine = start,
        EndLine = end,
        StructuralHash = $"s-{symbol}",
        SemanticHash = $"m-{symbol}",
        Content = $"# {symbol}\n\n## Responsibilities\n- Handles {symbol.ToLowerInvariant()} lifecycle for the sample domain.\n\n## Confidence\n{confidence:F2}",
        Confidence = confidence,
        ReviewStatus = confidence < 0.7 ? ReviewStatus.NeedsReview : ReviewStatus.None,
        CommitSha = "",
        AnalyzedAt = DateTimeOffset.UtcNow
    };
}

internal sealed class FakeRegistry(
    FakeChat? primary = null,
    FakeChat? fallback = null,
    FakeEmbedding? embedding = null) : IProviderRegistry
{
    private readonly FakeChat? _primary = primary;
    private readonly FakeChat? _fallback = fallback;
    private readonly FakeEmbedding? _embedding = embedding;

    public IChatProvider? Primary => _primary;
    public IChatProvider? LargeTier => null;
    public IChatProvider? Fallback => _fallback;
    public IEmbeddingProvider? Embedding => _embedding;
    public int Count => (_primary is not null ? 1 : 0) + (_fallback is not null ? 1 : 0);

    public IChatProvider? Get(string? name) => name switch
    {
        "primary" => _primary,
        "fallback" => _fallback,
        _ => null
    };
}

internal sealed class FakeChat : IChatProvider
{
    private readonly Func<IReadOnlyList<ChatMessage>, string> _handler;

    public FakeChat(string name, string model, Func<IReadOnlyList<ChatMessage>, string> handler)
    {
        Name = name;
        Model = model;
        _handler = handler;
    }

    public string Name { get; }
    public string Model { get; }
    public int Calls { get; private set; }

    public Task<string> CompleteAsync(IReadOnlyList<ChatMessage> messages, CancellationToken ct = default)
    {
        Calls++;
        return Task.FromResult(_handler(messages));
    }
}

internal sealed class FakeEmbedding : IEmbeddingProvider
{
    private readonly Func<string, float[]> _handler;

    public FakeEmbedding(Func<string, float[]> handler)
    {
        _handler = handler;
    }

    public string Name => "primary";
    public string EmbeddingModel => "embed-model";
    public int Calls { get; private set; }

    public Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        Calls++;
        return Task.FromResult(_handler(text));
    }
}
