using Tessera.Domain.Entities;
using Tessera.Domain.Enums;
using Tessera.Domain.Merkle;
using Tessera.Domain.Parsing;

namespace Tessera.Domain.Tests;

public class SnapshotComposerTests
{
    private static readonly Guid RepoId = Guid.NewGuid();

    private static ParseResult BuildParse(string serviceHash)
    {
        return new ParseResult
        {
            CommitSha = "abc123",
            Entities =
            {
                new ParsedEntity
                {
                    Key = "PaymentService",
                    Path = "src/Payments/PaymentService.cs",
                    Symbol = "PaymentService",
                    Kind = NodeKind.Class,
                    Language = "csharp",
                    StructuralHash = serviceHash
                },
                new ParsedEntity
                {
                    Key = "PaymentController",
                    Path = "src/Payments/PaymentController.cs",
                    Symbol = "PaymentController",
                    Kind = NodeKind.Class,
                    Language = "csharp",
                    StructuralHash = "ctrl-hash"
                }
            },
            Relationships =
            {
                new ParsedRelationship { From = "PaymentController", To = "PaymentService", Type = EdgeType.Calls }
            }
        };
    }

    [Fact]
    public void UnchangedNode_ReusesContentWithoutAi()
    {
        var parse = BuildParse("svc-hash");
        var first = SnapshotComposer.Compose(RepoId, Guid.NewGuid(), "abc123", parse, new Dictionary<string, KnowledgeNode>(), new Dictionary<string, AiContent>
        {
            ["PaymentService"] = new AiContent { Content = "# PaymentService", Confidence = 0.9, Model = "deepseek", PromptVersion = "1.0.0" },
            ["PaymentController"] = new AiContent { Content = "# PaymentController", Confidence = 0.9, Model = "deepseek", PromptVersion = "1.0.0" },
        });

        var second = SnapshotComposer.Compose(RepoId, Guid.NewGuid(), "abc124", parse, first.Nodes.ToDictionary(n => n.Key, n => n), new Dictionary<string, AiContent>());

        var before = first.Nodes.Single(n => n.Key == "PaymentService");
        var after = second.Nodes.Single(n => n.Key == "PaymentService");
        Assert.Equal(before.Content, after.Content);
        Assert.Equal(before.SemanticHash, after.SemanticHash);
    }

    [Fact]
    public void ChangedNode_AiContentReflected()
    {
        var parseV1 = BuildParse("svc-hash-v1");
        var first = SnapshotComposer.Compose(RepoId, Guid.NewGuid(), "abc123", parseV1, new Dictionary<string, KnowledgeNode>(), new Dictionary<string, AiContent>
        {
            ["PaymentService"] = new AiContent { Content = "old", Confidence = 0.9, Model = "deepseek", PromptVersion = "1.0.0" },
            ["PaymentController"] = new AiContent { Content = "ctrl", Confidence = 0.9, Model = "deepseek", PromptVersion = "1.0.0" },
        });

        var parseV2 = BuildParse("svc-hash-v2");
        var second = SnapshotComposer.Compose(RepoId, Guid.NewGuid(), "abc124", parseV2, first.Nodes.ToDictionary(n => n.Key, n => n), new Dictionary<string, AiContent>
        {
            ["PaymentService"] = new AiContent { Content = "new responsibilities", Confidence = 0.9, Model = "deepseek", PromptVersion = "1.0.0" },
        });

        Assert.Equal("old", first.Nodes.Single(n => n.Key == "PaymentService").Content);
        Assert.Equal("new responsibilities", second.Nodes.Single(n => n.Key == "PaymentService").Content);
        Assert.NotEqual(first.RootHash, second.RootHash);
    }

    [Fact]
    public void Cascade_ParentHashChangesWhenDependencyContentChanges()
    {
        var parse = BuildParse("svc-hash");
        var first = SnapshotComposer.Compose(RepoId, Guid.NewGuid(), "abc123", parse, new Dictionary<string, KnowledgeNode>(), new Dictionary<string, AiContent>
        {
            ["PaymentService"] = new AiContent { Content = "v1", Confidence = 0.9, Model = "deepseek", PromptVersion = "1.0.0" },
            ["PaymentController"] = new AiContent { Content = "ctrl", Confidence = 0.9, Model = "deepseek", PromptVersion = "1.0.0" },
        });

        // same structure, only AI content of service changes (regenerate)
        var parseV2 = BuildParse("svc-hash");
        var second = SnapshotComposer.Compose(RepoId, Guid.NewGuid(), "abc124", parseV2, first.Nodes.ToDictionary(n => n.Key, n => n), new Dictionary<string, AiContent>
        {
            ["PaymentService"] = new AiContent { Content = "v2", Confidence = 0.95, Model = "deepseek", PromptVersion = "1.1.0" },
        });

        var serviceBefore = first.Nodes.Single(n => n.Key == "PaymentService").SemanticHash;
        var serviceAfter = second.Nodes.Single(n => n.Key == "PaymentService").SemanticHash;
        var ctrlBefore = first.Nodes.Single(n => n.Key == "PaymentController").SemanticHash;
        var ctrlAfter = second.Nodes.Single(n => n.Key == "PaymentController").SemanticHash;

        Assert.NotEqual(serviceBefore, serviceAfter);
        Assert.NotEqual(ctrlBefore, ctrlAfter);
    }
}
