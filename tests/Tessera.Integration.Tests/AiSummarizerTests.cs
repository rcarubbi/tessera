using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tessera.Domain.Parsing;
using Tessera.Domain.Ports;
using Tessera.Infrastructure.Ai;

namespace Tessera.Integration.Tests;

public sealed class AiSummarizerTests
{
    [Fact]
    public async Task Uses_primary_provider_and_parses_confidence()
    {
        var primary = new FakeChatProvider("deepseek", "deepseek-chat", _ => "# Order\n\n## Responsibilities\n- X\n\nConfidence: 0.85");
        var sut = Create(primary, fallback: null);

        var result = await sut.SummarizeAsync(Entity(), relationships: [], repositoryId: 1);

        Assert.Equal("deepseek/deepseek-chat", result.Model);
        Assert.Equal(AiSummarizer.PromptVersionConst, result.PromptVersion);
        Assert.Equal(0.85, result.Confidence);
        Assert.Contains("# Order", result.Content);
        Assert.Equal(1, primary.Calls);
    }

    [Fact]
    public async Task Falls_back_to_secondary_provider_on_failure()
    {
        var primary = new FakeChatProvider("deepseek", "deepseek-chat", _ => throw new ChatProviderException("boom"));
        var fallback = new FakeChatProvider("qwen", "qwen-plus", _ => "## Type\nClass\n\nConfidence: 0.5");
        var sut = Create(primary, fallback);

        var result = await sut.SummarizeAsync(Entity(), relationships: [], repositoryId: 1);

        Assert.Equal("qwen/qwen-plus", result.Model);
        Assert.Equal(0.5, result.Confidence);
        Assert.Equal(1, primary.Calls);
        Assert.Equal(1, fallback.Calls);
    }

    [Fact]
    public async Task Falls_back_to_rule_based_when_all_providers_fail()
    {
        var primary = new FakeChatProvider("deepseek", "deepseek-chat", _ => throw new ChatProviderException("boom"));
        var fallback = new FakeChatProvider("qwen", "qwen-plus", _ => throw new ChatProviderException("also boom"));
        var sut = Create(primary, fallback);

        var result = await sut.SummarizeAsync(Entity(), relationships: [], repositoryId: 1);

        Assert.Equal("rule-based", result.Model);
        Assert.Equal(0.60, result.Confidence);
    }

    [Fact]
    public async Task Uses_rule_based_when_no_provider_configured()
    {
        var sut = Create(primary: null, fallback: null);

        var result = await sut.SummarizeAsync(Entity(), relationships: [], repositoryId: 1);

        Assert.Equal("rule-based", result.Model);
    }

    [Fact]
    public async Task Routes_complex_entity_to_large_tier()
    {
        var primary = new FakeChatProvider("deepseek", "deepseek-chat", _ => "Confidence: 0.8");
        var large = new FakeChatProvider("deepseek-large", "deepseek-reasoner", _ => "Confidence: 0.9");
        var registry = new FakeProviderRegistry(primary, large, large);

        var sut = new AiSummarizer(
            registry,
            new RuleBasedSummarizer(),
            new TokenBudgetTracker(Options.Create(new AiOptions { DailyBudgetTokens = 10_000_000 })),
            Options.Create(new AiOptions { ComplexityThresholdLines = 200 }),
            NullLogger<AiSummarizer>.Instance);

        var small = Entity();
        var complex = Entity(EndLine: 500);

        await sut.SummarizeAsync(small, relationships: [], repositoryId: 1);
        await sut.SummarizeAsync(complex, relationships: [], repositoryId: 1);

        Assert.Equal(1, primary.Calls);
        Assert.Equal(1, large.Calls);
    }

    [Fact]
    public async Task Returns_rule_based_when_daily_budget_exhausted()
    {
        var primary = new FakeChatProvider("deepseek", "deepseek-chat", _ => "Confidence: 0.8");
        var budget = new TokenBudgetTracker(Options.Create(new AiOptions { DailyBudgetTokens = 100 }));
        var sut = Create(primary, fallback: null, budget: budget);

        var result = await sut.SummarizeAsync(Entity(), relationships: [], repositoryId: 1);

        Assert.Equal("rule-based", result.Model);
        Assert.Equal(0, primary.Calls);
    }

    [Fact]
    public async Task Extracts_class_diagram_and_strips_diagram_section()
    {
        var provider = new FakeChatProvider("deepseek", "deepseek-chat", _ =>
            "# Order\n\n## Responsibilities\n- X\n\n## Class diagram\n\n```mermaid\nclassDiagram\n    class Order\n```\n\nConfidence: 0.8");
        var sut = Create(provider, fallback: null);

        var result = await sut.SummarizeAsync(Entity(), relationships: [], repositoryId: 1);

        Assert.Equal("classDiagram\n    class Order", result.ClassDiagram);
        Assert.Null(result.SequenceDiagram);
        Assert.DoesNotContain("Class diagram", result.Content);
        Assert.Contains("## Responsibilities", result.Content);
        Assert.Equal(0.8, result.Confidence);
    }

    [Fact]
    public async Task Extracts_sequence_diagram_for_method_entity()
    {
        var method = new ParsedEntity
        {
            Key = "Order.cs::Create",
            Path = "Order.cs",
            Symbol = "Create",
            Kind = Tessera.Domain.Enums.NodeKind.Method,
            Language = "csharp",
            StartLine = 1,
            EndLine = 10,
            StructuralHash = "abc"
        };
        var provider = new FakeChatProvider("deepseek", "deepseek-chat", _ =>
            "## Responsibilities\n- Y\n\n## Sequence diagram\n\n```mermaid\nsequenceDiagram\n    self->>Dep: invoke\n```\n\nConfidence: 0.7");
        var sut = Create(provider, fallback: null);

        var result = await sut.SummarizeAsync(method, relationships: [], repositoryId: 1);

        Assert.Equal("sequenceDiagram\n    self->>Dep: invoke", result.SequenceDiagram);
        Assert.Null(result.ClassDiagram);
        Assert.Equal(0.7, result.Confidence);
    }

    [Fact]
    public async Task Rule_based_fallback_emits_diagram_placeholders()
    {
        var sut = Create(primary: null, fallback: null);

        var result = await sut.SummarizeAsync(Entity(), relationships: [], repositoryId: 1);

        Assert.Equal("rule-based", result.Model);
        Assert.NotNull(result.ClassDiagram);
        Assert.Contains("classDiagram", result.ClassDiagram);
        Assert.Contains("## Known issues", result.Content);
        Assert.Contains("## Error handling", result.Content);
        Assert.Contains("## State management", result.Content);
    }

    private static AiSummarizer Create(
        FakeChatProvider? primary,
        FakeChatProvider? fallback,
        TokenBudgetTracker? budget = null)
    {
        var registry = new FakeProviderRegistry(
            primary ?? new FakeChatProvider("empty", "empty", _ => ""),
            primary,
            fallback);
        return new AiSummarizer(
            registry,
            new RuleBasedSummarizer(),
            budget ?? new TokenBudgetTracker(Options.Create(new AiOptions { DailyBudgetTokens = 10_000_000 })),
            Options.Create(new AiOptions
            {
                ComplexityThresholdLines = 200,
                DailyBudgetTokens = 10_000_000
            }),
            NullLogger<AiSummarizer>.Instance);
    }

    private static ParsedEntity Entity(int StartLine = 1, int EndLine = 10) => new()
    {
        Key = "Order.cs::Order",
        Path = "Order.cs",
        Symbol = "Order",
        Kind = Tessera.Domain.Enums.NodeKind.Class,
        Language = "csharp",
        StartLine = StartLine,
        EndLine = EndLine,
        StructuralHash = "abc"
    };

    private sealed class FakeProviderRegistry(
        FakeChatProvider primary,
        FakeChatProvider? large,
        FakeChatProvider? fallback)
        : IProviderRegistry
    {
        private readonly FakeChatProvider _primary = primary;
        private readonly FakeChatProvider? _fallback = fallback;

        public IChatProvider? Primary => _primary;
        public IChatProvider? LargeTier => large;
        public IChatProvider? Fallback => _fallback;
        public IEmbeddingProvider? Embedding => null;
        public int Count => 3;

        public IChatProvider? Get(string? name) => name switch
        {
            "deepseek" => _primary,
            "qwen" => _fallback,
            _ => null
        };
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

public sealed class TokenBudgetTrackerTests
{
    [Fact]
    public void Exhausts_budget_and_resets_next_day()
    {
        var tracker = new TokenBudgetTracker(Options.Create(new AiOptions { DailyBudgetTokens = 100 }));
        var day = new DateTimeOffset(2026, 8, 6, 12, 0, 0, TimeSpan.Zero);

        Assert.True(tracker.TryAllocate(1, 60, day));
        Assert.True(tracker.TryAllocate(1, 40, day));
        Assert.False(tracker.TryAllocate(1, 10, day));
        Assert.Equal(100, tracker.Used(1, day));

        var nextDay = new DateTimeOffset(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);
        Assert.True(tracker.TryAllocate(1, 90, nextDay));
    }
}
