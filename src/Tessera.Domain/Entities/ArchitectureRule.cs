using Tessera.Domain.Enums;

namespace Tessera.Domain.Entities;

public enum RuleConstraintKind
{
    Deny,
    Require
}

public sealed record NodeSelector(string? PathPrefix, NodeKind? Kind)
{
    public bool IsEmpty => string.IsNullOrWhiteSpace(PathPrefix) && Kind is null;
}

public sealed record RuleConstraint(RuleConstraintKind Kind, NodeSelector From, NodeSelector? To);

public sealed record ArchitectureRule(string Name, RuleSeverity Severity, RuleConstraint Constraint);

public sealed record ArchitectureRuleSet(IReadOnlyList<ArchitectureRule> Rules);

public sealed record RuleViolation(
    string RuleName,
    RuleSeverity Severity,
    string FromKey,
    string ToKey,
    string FromPath,
    int FromLine,
    string ToPath,
    int ToLine,
    EdgeType? EdgeType,
    double Confidence,
    bool LowConfidence)
{
    public bool IsMissingRequirement => string.IsNullOrEmpty(FromKey) && string.IsNullOrEmpty(ToKey);
}
