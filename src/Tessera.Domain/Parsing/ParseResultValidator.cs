namespace Tessera.Domain.Parsing;

// Thrown for structurally invalid parser output that would silently corrupt hashing or persisted graph data.
public sealed class ParseResultValidationException(IReadOnlyList<string> errors)
    : Exception($"Parse result validation failed: {string.Join("; ", errors)}")
{
    public IReadOnlyList<string> Errors { get; } = errors;
}

public static class ParseResultValidator
{
    /// <summary>
    /// Validates a <see cref="ParseResult"/> before it is used to compose a snapshot.
    /// Throws <see cref="ParseResultValidationException"/> for structural defects (duplicate/empty keys,
    /// invalid line ranges, out-of-range confidence). Relationships that reference an entity key not present
    /// in this parse result are intentionally tolerated (parsers may reference unresolved external symbols)
    /// and are returned as diagnostics instead of errors.
    /// </summary>
    public static IReadOnlyList<string> Validate(ParseResult parse)
    {
        var errors = new List<string>();
        var diagnostics = new List<string>();
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entity in parse.Entities)
        {
            if (string.IsNullOrWhiteSpace(entity.Key))
            {
                errors.Add($"Entity at '{entity.Path}' has an empty key.");
                continue;
            }

            if (!seenKeys.Add(entity.Key))
            {
                errors.Add($"Duplicate entity key '{entity.Key}'.");
            }

            if (entity.StartLine < 0 || entity.EndLine < entity.StartLine)
            {
                errors.Add($"Entity '{entity.Key}' has an invalid line range ({entity.StartLine}-{entity.EndLine}).");
            }
        }

        foreach (var rel in parse.Relationships)
        {
            if (string.IsNullOrWhiteSpace(rel.From) || string.IsNullOrWhiteSpace(rel.To))
            {
                errors.Add($"Relationship of type '{rel.Type}' has an empty endpoint key.");
                continue;
            }

            if (rel.Confidence is < 0.0 or > 1.0)
            {
                errors.Add($"Relationship {rel.From}->{rel.To} ({rel.Type}) has an out-of-range confidence value ({rel.Confidence}).");
            }

            if (!seenKeys.Contains(rel.From) || !seenKeys.Contains(rel.To))
            {
                diagnostics.Add($"Relationship {rel.From}->{rel.To} ({rel.Type}) references an entity not present in this parse result and will be ignored.");
            }
        }

        if (errors.Count > 0)
        {
            throw new ParseResultValidationException(errors);
        }

        return diagnostics;
    }
}
