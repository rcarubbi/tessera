using System.Text;
using Tessera.Domain.Enums;
using Tessera.Domain.Merkle;
using Tessera.Domain.Parsing;

namespace Tessera.Infrastructure.Ai;

public sealed class RuleBasedSummarizer : ISemanticSummarizer
{
    public const string PromptVersionConst = "0.0.0";

    public string PromptVersion => PromptVersionConst;

    public Task<AiContent> SummarizeAsync(
        ParsedEntity entity,
        IReadOnlyList<ParsedRelationship> relationships,
        long repositoryId,
        CancellationToken ct = default)
        => Task.FromResult(Summarize(entity, relationships));

    public AiContent Summarize(ParsedEntity entity, IReadOnlyList<ParsedRelationship> relationships)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {entity.Symbol}");
        sb.AppendLine();
        sb.AppendLine("## Type");
        sb.AppendLine($"{KindLabel(entity.Kind)}");
        sb.AppendLine();
        sb.AppendLine("## Responsibilities");
        sb.AppendLine("- Encapsulates `" + entity.Symbol + "` (structural analysis; semantic summary pending AI provider)");
        sb.AppendLine();
        sb.AppendLine($"- Source: `{entity.Path}` lines {entity.StartLine}-{entity.EndLine}");

        var dependencies = relationships
            .Where(r => r.From == entity.Key)
            .Select(r => r.To)
            .Distinct()
            .ToList();
        if (dependencies.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Dependencies");
            foreach (var dep in dependencies)
            {
                sb.AppendLine($"- {dep}");
            }
        }

        var consumers = relationships
            .Where(r => r.To == entity.Key)
            .Select(r => r.From)
            .Distinct()
            .ToList();
        if (consumers.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Incoming references");
            foreach (var consumer in consumers)
            {
                sb.AppendLine($"- {consumer}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("## Confidence");
        sb.AppendLine("0.60 (structural only)");

        return new AiContent
        {
            Content = sb.ToString(),
            Confidence = 0.60,
            Model = "rule-based",
            PromptVersion = PromptVersion
        };
    }

    private static string KindLabel(NodeKind kind) => kind switch
    {
        NodeKind.Class => "Class",
        NodeKind.Interface => "Interface",
        NodeKind.Struct => "Struct",
        NodeKind.Enum => "Enum",
        NodeKind.Record => "Record",
        NodeKind.Method => "Method",
        NodeKind.Function => "Function",
        NodeKind.Module => "Module",
        NodeKind.Property => "Property",
        NodeKind.Event => "Event",
        _ => kind.ToString()
    };
}
