using System.Text;
using Tessera.Domain.Enums;
using Tessera.Domain.Merkle;
using Tessera.Domain.Parsing;

namespace Tessera.Infrastructure.Ai;

public sealed class RuleBasedSummarizer : ISemanticSummarizer
{
    public const string PromptVersionConst = "0.1.0";

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

        RuleBasedArchitect.AppendSection(sb, entity);

        sb.AppendLine();
        sb.AppendLine("## Error handling");
        sb.AppendLine("- Not analyzed (structural only; AI provider unavailable)");
        sb.AppendLine();
        sb.AppendLine("## State management");
        sb.AppendLine("- Not analyzed (structural only; AI provider unavailable)");
        sb.AppendLine();
        sb.AppendLine("## Known issues");
        sb.AppendLine("- None identified (structural analysis only)");

        sb.AppendLine();
        sb.AppendLine("## Confidence");
        sb.AppendLine("0.60 (structural only)");

        var isMethod = entity.Kind is NodeKind.Method or NodeKind.Function;
        return new AiContent
        {
            Content = sb.ToString(),
            ClassDiagram = isMethod ? null : BuildClassDiagram(entity, relationships),
            SequenceDiagram = isMethod ? BuildSequenceDiagram(entity, relationships) : null,
            Confidence = 0.60,
            Model = "rule-based",
            PromptVersion = PromptVersion
        };
    }

    private static string BuildClassDiagram(ParsedEntity entity, IReadOnlyList<ParsedRelationship> relationships)
    {
        var deps = relationships
            .Where(r => r.From == entity.Key)
            .Select(r => r.To)
            .Distinct()
            .Take(8)
            .ToList();
        var sb = new StringBuilder();
        sb.AppendLine("classDiagram");
        sb.AppendLine($"    class {MermaidId(entity.Symbol)} {{");
        sb.AppendLine($"        +{KindLabel(entity.Kind)}");
        sb.AppendLine("    }");
        foreach (var dep in deps)
        {
            sb.AppendLine($"    {MermaidId(entity.Symbol)} --> {MermaidId(dep)}");
        }
        return sb.ToString();
    }

    private static string BuildSequenceDiagram(ParsedEntity entity, IReadOnlyList<ParsedRelationship> relationships)
    {
        var calls = relationships
            .Where(r => r.From == entity.Key && r.Type == EdgeType.Calls)
            .Select(r => r.To)
            .Distinct()
            .Take(8)
            .ToList();
        var sb = new StringBuilder();
        sb.AppendLine("sequenceDiagram");
        sb.AppendLine($"    participant self as {entity.Symbol}");
        foreach (var call in calls)
        {
            sb.AppendLine($"    self->>{MermaidId(call)}: invoke");
        }
        return sb.ToString();
    }

    private static string MermaidId(string key)
    {
        var sanitized = new string(key.Where(char.IsLetterOrDigit).ToArray());
        return sanitized.Length > 0 ? sanitized[..Math.Min(sanitized.Length, 40)] : "entity";
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
