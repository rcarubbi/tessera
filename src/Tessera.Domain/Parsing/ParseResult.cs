using Tessera.Domain.Enums;

namespace Tessera.Domain.Parsing;

public sealed class ParsedEntity
{
    public string Key { get; set; } = "";
    public string Path { get; set; } = "";
    public string Symbol { get; set; } = "";
    public NodeKind Kind { get; set; }
    public string Language { get; set; } = "";
    public int StartLine { get; set; }
    public int EndLine { get; set; }
    public string StructuralHash { get; set; } = "";
    public string? Signature { get; set; }
}

public sealed class ParsedRelationship
{
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public EdgeType Type { get; set; }
    public string? Evidence { get; set; }
    public double Confidence { get; set; } = 1.0;
    public bool IsStatic { get; set; } = true;
}

public sealed class ParseResult
{
    public string CommitSha { get; set; } = "";
    public string DefaultBranch { get; set; } = "";
    public List<ParsedEntity> Entities { get; set; } = new();
    public List<ParsedRelationship> Relationships { get; set; } = new();
}
