using System.Text;
using Tessera.Domain.Enums;
using Tessera.Domain.Parsing;

namespace Tessera.Infrastructure.Ai;

public sealed record ArchitectureInfo(string Context, string Role);

public static class RuleBasedArchitect
{
    private static readonly string[] ContainerSegments =
        ["src", "app", "lib", "main", "source", "test", "tests", "packages", "services", "api", "web", "infra", "infrastructure"];

    public static ArchitectureInfo Infer(ParsedEntity entity)
        => new(InferContext(entity.Path), InferRole(entity));

    public static string InferContext(string path)
    {
        var segments = path
            .Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();

        var fileIndex = segments.FindLastIndex(s => s.Contains('.'));
        var folders = fileIndex >= 0 ? segments.Take(fileIndex).ToList() : segments;
        var meaningful = folders.SkipWhile(s => ContainerSegments.Contains(s.ToLowerInvariant())).ToList();

        return meaningful.FirstOrDefault() is { } context ? ToPascalCase(context) : "Root";
    }

    public static string InferRole(ParsedEntity entity)
    {
        if (entity.Kind is NodeKind.Method or NodeKind.Function or NodeKind.Property or NodeKind.Event)
        {
            return "Member";
        }
        if (entity.Kind == NodeKind.Interface)
        {
            return "Contract";
        }
        if (entity.Kind == NodeKind.Enum)
        {
            return "Enumeration";
        }
        if (entity.Kind == NodeKind.Record)
        {
            return "DataRecord";
        }
        if (entity.Kind == NodeKind.Struct)
        {
            return "ValueObject";
        }

        var name = entity.Symbol;
        if (name.EndsWith("Controller", StringComparison.OrdinalIgnoreCase)) return "Controller";
        if (name.EndsWith("Service", StringComparison.OrdinalIgnoreCase)) return "Service";
        if (name.EndsWith("Repository", StringComparison.OrdinalIgnoreCase)) return "Repository";
        if (name.EndsWith("Repo", StringComparison.OrdinalIgnoreCase)) return "Repository";
        if (name.EndsWith("Endpoint", StringComparison.OrdinalIgnoreCase)) return "Endpoint";
        if (name.EndsWith("Handler", StringComparison.OrdinalIgnoreCase)) return "Handler";
        if (name.EndsWith("Provider", StringComparison.OrdinalIgnoreCase)) return "Provider";
        if (name.EndsWith("EventPublisher", StringComparison.OrdinalIgnoreCase) || name.EndsWith("Publisher", StringComparison.OrdinalIgnoreCase)) return "EventPublisher";
        if (name.EndsWith("EventConsumer", StringComparison.OrdinalIgnoreCase) || name.EndsWith("Subscriber", StringComparison.OrdinalIgnoreCase)) return "EventConsumer";
        if (name.EndsWith("Configuration", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("Options", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("Settings", StringComparison.OrdinalIgnoreCase)) return "Configuration";
        if (name.EndsWith("Dto", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("Request", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("Response", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("ViewModel", StringComparison.OrdinalIgnoreCase)) return "DTO";

        return entity.Kind == NodeKind.Class ? "Domain" : "Component";
    }

    public static void AppendSection(StringBuilder sb, ParsedEntity entity)
    {
        var info = Infer(entity);
        sb.AppendLine();
        sb.AppendLine("## Architecture");
        sb.AppendLine($"- Bounded context: {info.Context}");
        sb.AppendLine($"- Role: {info.Role}");
    }

    private static string ToPascalCase(string segment)
        => string.Concat(segment
            .Split(['-', '_', ' '], StringSplitOptions.RemoveEmptyEntries)
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
}
