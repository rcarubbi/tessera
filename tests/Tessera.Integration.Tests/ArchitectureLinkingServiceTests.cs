using Microsoft.Extensions.Options;
using Tessera.Domain.Enums;
using Tessera.Domain.Parsing;
using Tessera.Domain.Ports;
using Tessera.Infrastructure.Ai;
using Tessera.Infrastructure.Analysis;

namespace Tessera.Integration.Tests;

public sealed class ArchitectureLinkingServiceTests
{
    [Fact]
    public async Task Adds_cross_technology_edges_from_llm_response()
    {
        var provider = new FakeChatProvider("deepseek", "deepseek-chat", _ =>
            """
            [
              {
                "from": "web/api/orders.ts::OrdersClient",
                "to": "OrdersController.cs::OrdersController",
                "type": "InvokesEndpoint",
                "evidence": "fetch('/api/orders') calls the C# OrdersController route",
                "confidence": 0.85
              }
            ]
            """);
        var svc = Create(provider);

        var edges = await svc.LinkAsync(MixedParse(), repositoryId: 1);

        var edge = Assert.Single(edges);
        Assert.Equal("web/api/orders.ts::OrdersClient", edge.From);
        Assert.Equal("OrdersController.cs::OrdersController", edge.To);
        Assert.Equal(EdgeType.InvokesEndpoint, edge.Type);
        Assert.Equal(0.85, edge.Confidence);
        Assert.Equal(1, provider.Calls);
    }

    [Fact]
    public async Task Skips_single_language_repositories_without_calling_llm()
    {
        var provider = new FakeChatProvider("deepseek", "deepseek-chat", _ => throw new Exception("should not be called"));
        var svc = Create(provider);
        var parse = new ParseResult();
        parse.Entities.Add(Entity("Order.cs::Order", "Order", "c_sharp"));

        var edges = await svc.LinkAsync(parse, repositoryId: 1);

        Assert.Empty(edges);
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task Filters_unknown_keys_and_unrecognized_types()
    {
        var provider = new FakeChatProvider("deepseek", "deepseek-chat", _ =>
            """
            [
              { "from": "ghost.cs::Ghost", "to": "OrdersController.cs::OrdersController", "type": "Calls", "evidence": "?", "confidence": 0.6 },
              { "from": "web/api/orders.ts::OrdersClient", "to": "OrdersController.cs::OrdersController", "type": "Inherits", "evidence": "?", "confidence": 0.9 },
              { "from": "web/api/orders.ts::OrdersClient", "to": "OrdersController.cs::OrdersController", "type": "Calls", "evidence": "ok", "confidence": 0.8 },
              { "from": "web/api/orders.ts::OrdersClient", "to": "OrdersController.cs::OrdersController", "type": "Calls", "evidence": "duplicate", "confidence": 0.8 }
            ]
            """);
        var svc = Create(provider);

        var edges = await svc.LinkAsync(MixedParse(), repositoryId: 1);

        var edge = Assert.Single(edges);
        Assert.Equal("web/api/orders.ts::OrdersClient", edge.From);
        Assert.Equal(EdgeType.Calls, edge.Type);
    }

    [Fact]
    public async Task Skips_edges_already_found_by_static_analysis()
    {
        var provider = new FakeChatProvider("deepseek", "deepseek-chat", _ =>
            """
            [
              { "from": "web/api/orders.ts::OrdersClient", "to": "OrdersController.cs::OrdersController", "type": "Calls", "evidence": "dup", "confidence": 0.8 }
            ]
            """);
        var svc = Create(provider);
        var parse = MixedParse();
        parse.Relationships.Add(new ParsedRelationship
        {
            From = "web/api/orders.ts::OrdersClient",
            To = "OrdersController.cs::OrdersController",
            Type = EdgeType.Calls
        });

        var edges = await svc.LinkAsync(parse, repositoryId: 1);

        Assert.Empty(edges);
    }

    private static ArchitectureLinkingService Create(FakeChatProvider primary)
    {
        var registry = new FakeProviderRegistry(primary);
        return new ArchitectureLinkingService(
            registry,
            new TokenBudgetTracker(Options.Create(new AiOptions { DailyBudgetTokens = 10_000_000 })),
            Options.Create(new AiOptions { MaxRetries = 1, DailyBudgetTokens = 10_000_000 }));
    }

    private static ParseResult MixedParse()
    {
        var parse = new ParseResult();
        parse.Entities.Add(Entity("OrdersController.cs::OrdersController", "OrdersController", "c_sharp"));
        parse.Entities.Add(Entity("web/api/orders.ts::OrdersClient", "OrdersClient", "typescript"));
        return parse;
    }

    private static ParsedEntity Entity(string key, string symbol, string language) => new()
    {
        Key = key,
        Path = key.Split("::")[0],
        Symbol = symbol,
        Kind = NodeKind.Class,
        Language = language,
        StartLine = 1,
        EndLine = 40,
        StructuralHash = "abc"
    };

    private sealed class FakeProviderRegistry(FakeChatProvider primary) : IProviderRegistry
    {
        public IChatProvider? Primary => primary;
        public IChatProvider? LargeTier => null;
        public IChatProvider? Fallback => null;
        public IEmbeddingProvider? Embedding => null;
        public int Count => 1;
        public IChatProvider? Get(string? name) => primary.Name == name ? primary : null;
    }

    private sealed class FakeChatProvider : IChatProvider
    {
        private readonly Func<IReadOnlyList<ChatMessage>, string> _handler;
        public FakeChatProvider(string name, string model, Func<IReadOnlyList<ChatMessage>, string> handler)
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
}
