using Microsoft.Extensions.Options;
using Tessera.Domain.Entities;
using Tessera.Domain.Enums;
using Tessera.Domain.Ports;
using Tessera.Infrastructure.Ai;
using Tessera.Infrastructure.Chat;

namespace Tessera.Integration.Tests;

public sealed class OverviewServiceTests
{
    [Fact]
    public async Task Rule_based_overview_contains_mermaid_component_diagram()
    {
        var svc = Create(primary: null);

        var result = await svc.GenerateAsync(Repo(), Nodes(), Edges());

        Assert.Equal("rule-based", result.Model);
        Assert.Equal(2, result.NodeCount);
        Assert.Contains("```mermaid", result.Overview);
        Assert.Contains("flowchart LR", result.Overview);
        Assert.Contains("subgraph", result.Overview);
        Assert.Contains(".NET / C#", result.Overview);
        Assert.Contains("TypeScript / React", result.Overview);
    }

    [Fact]
    public async Task Ai_overview_replaces_ai_diagram_with_rule_based_and_keeps_prose()
    {
        var provider = new FakeChatProvider("deepseek", "deepseek-chat", _ =>
            "## Summary\nok\n\n## Main components\n- [Order.cs::Order] Order\n\n## Component diagram\n\n```mermaid\nflowchart LR\n  n0[Order]\n```");
        var svc = Create(provider);
        var result = await svc.GenerateAsync(Repo(), Nodes(), Edges());
        var overview = result.Overview.Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Equal("deepseek/deepseek-chat", result.Model);
        Assert.Contains("## Summary\nok", overview);
        Assert.Contains("```mermaid\nflowchart LR", overview);
        Assert.Contains("subgraph", overview);
        Assert.Contains("n0[\"Order\"]", overview);
        Assert.DoesNotContain("n0[Order]", overview);
        Assert.Equal(1, provider.Calls);
    }

    [Fact]
    public async Task Returns_none_when_no_nodes()
    {
        var svc = Create(primary: null);
        var result = await svc.GenerateAsync(Repo(), nodes: []);
        Assert.Equal("none", result.Model);
        Assert.Equal(0, result.NodeCount);
    }

    private static OverviewService Create(FakeChatProvider? primary)
    {
        return new OverviewService(
            new FakeProviderRegistry(primary),
            new TokenBudgetTracker(Options.Create(new AiOptions { DailyBudgetTokens = 10_000_000 })),
            Options.Create(new AiOptions
            {
                MaxRetries = 1,
                ComplexityThresholdLines = 200,
                DailyBudgetTokens = 10_000_000
            }));
    }

    private static Repository Repo() => new() { Id = Guid.NewGuid(), GitHubId = 1 };

    private static IReadOnlyList<KnowledgeNode> Nodes() =>
    [
        new()
        {
            Id = Guid.NewGuid(),
            Key = "Order.cs::Order",
            Path = "Order.cs",
            Symbol = "Order",
            Kind = NodeKind.Class,
            Language = "c_sharp",
            StartLine = 1,
            EndLine = 50,
            Confidence = 0.9,
            Content = "- Architecture: Domain entity\n- Bounded context: Orders\n- Handles order lifecycle"
        },
        new()
        {
            Id = Guid.NewGuid(),
            Key = "api/orders.ts::OrdersClient",
            Path = "api/orders.ts",
            Symbol = "OrdersClient",
            Kind = NodeKind.Class,
            Language = "tsx",
            StartLine = 1,
            EndLine = 40,
            Confidence = 0.8,
            Content = "- Architecture: API client\n- Bounded context: Orders\n- Fetches orders from the backend"
        }
    ];

    private static IReadOnlyList<GraphEdge>? Edges() =>
    [
        new()
        {
            FromKey = "api/orders.ts::OrdersClient",
            ToKey = "Order.cs::Order",
            Type = EdgeType.InvokesEndpoint,
            Confidence = 0.7
        }
    ];

    private sealed class FakeProviderRegistry(FakeChatProvider? primary) : IProviderRegistry
    {
        private readonly FakeChatProvider? _primary = primary;
        public IChatProvider? Primary => _primary;
        public IChatProvider? LargeTier => null;
        public IChatProvider? Fallback => null;
        public IEmbeddingProvider? Embedding => null;
        public int Count => _primary is null ? 0 : 1;
        public IChatProvider? Get(string? name) => _primary?.Name == name ? _primary : null;
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
