using Microsoft.EntityFrameworkCore;
using Tessera.Domain.Entities;
using Tessera.Infrastructure.Analysis;
using Tessera.Infrastructure.Data;
using Tessera.Infrastructure.Queries;

namespace Tessera.Api;

public sealed record RuleSelectorResponse(string? Path, string? Kind);
public sealed record RuleConstraintResponse(string Kind, RuleSelectorResponse From, RuleSelectorResponse? To);
public sealed record RuleResponse(string Name, string Severity, RuleConstraintResponse Constraint);
public sealed record RuleSetResponse(string Yaml, IReadOnlyList<RuleResponse> Rules);
public sealed record RuleViolationResponse(
    string RuleName,
    string Severity,
    string FromKey,
    string ToKey,
    string FromPath,
    int FromLine,
    string ToPath,
    int ToLine,
    string? EdgeType,
    double Confidence,
    bool LowConfidence,
    bool IsMissingRequirement);
public sealed record RuleEvaluationResponse(string CommitSha, IReadOnlyList<RuleViolationResponse> Violations);
public sealed record RuleDriftEntryResponse(
    string RuleName,
    string Severity,
    string FromKey,
    string ToKey,
    string FromPath,
    string ToPath,
    string? EdgeType,
    string IntroducedCommit,
    bool IsLive,
    bool LowConfidence);
public sealed record RuleDriftResponse(string FromCommit, string ToCommit, IReadOnlyList<RuleDriftEntryResponse> Entries);

public static class RuleEndpoints
{
    public static void MapRuleEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/repositories/{repositoryId:guid}/rules", async (
            Guid repositoryId,
            HttpContext context,
            TesseraDbContext db,
            CancellationToken ct) =>
        {
            var guarded = await context.GuardRepoAsync(db, repositoryId, ct);
            if (guarded is not null) return guarded;

            var repo = await db.Repositories.AsNoTracking().FirstOrDefaultAsync(r => r.Id == repositoryId, ct);
            if (repo is null)
            {
                return Results.NotFound(new { error = "Repository not found" });
            }

            return Results.Ok(BuildRuleSetResponse(repo.RulesYaml));
        });

        app.MapPut("/api/repositories/{repositoryId:guid}/rules", async (
            Guid repositoryId,
            RulesRequest? request,
            HttpContext context,
            TesseraDbContext db,
            CancellationToken ct) =>
        {
            var guarded = await context.GuardRepoAsync(db, repositoryId, ct);
            if (guarded is not null) return guarded;

            var repo = await db.Repositories.FirstOrDefaultAsync(r => r.Id == repositoryId, ct);
            if (repo is null)
            {
                return Results.NotFound(new { error = "Repository not found" });
            }

            var yaml = request?.Yaml ?? "";
            try
            {
                ArchitectureRuleService.Parse(yaml);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }

            repo.RulesYaml = string.IsNullOrWhiteSpace(yaml) ? null : yaml;
            repo.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);

            return Results.Ok(BuildRuleSetResponse(repo.RulesYaml));
        });

        app.MapGet("/api/repositories/{repositoryId:guid}/rules/violations", async (
            Guid repositoryId,
            string? commitSha,
            HttpContext context,
            TesseraDbContext db,
            ArchitectureRuleService rules,
            CancellationToken ct) =>
        {
            var guarded = await context.GuardRepoAsync(db, repositoryId, ct);
            if (guarded is not null) return guarded;

            var ruleSet = await LoadRuleSetAsync(db, repositoryId, ct);
            if (ruleSet is null)
            {
                return Results.NotFound(new { error = "No rules defined for this repository." });
            }

            try
            {
                var result = await rules.EvaluateAsync(repositoryId, ruleSet, commitSha, ct);
                return Results.Ok(new RuleEvaluationResponse(
                    result.CommitSha,
                    result.Violations.Select(ToResponse).ToList()));
            }
            catch (SnapshotNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        });

        app.MapGet("/api/repositories/{repositoryId:guid}/rules/drift", async (
            Guid repositoryId,
            string? from,
            string? to,
            HttpContext context,
            TesseraDbContext db,
            ArchitectureRuleService rules,
            CancellationToken ct) =>
        {
            var guarded = await context.GuardRepoAsync(db, repositoryId, ct);
            if (guarded is not null) return guarded;

            var ruleSet = await LoadRuleSetAsync(db, repositoryId, ct);
            if (ruleSet is null)
            {
                return Results.NotFound(new { error = "No rules defined for this repository." });
            }

            try
            {
                var result = await rules.DriftAsync(repositoryId, ruleSet, from, to, ct);
                return Results.Ok(new RuleDriftResponse(
                    result.FromCommit,
                    result.ToCommit,
                    result.Entries.Select(ToResponse).ToList()));
            }
            catch (SnapshotNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        });
    }

    private static async Task<ArchitectureRuleSet?> LoadRuleSetAsync(TesseraDbContext db, Guid repositoryId, CancellationToken ct)
    {
        var yaml = await db.Repositories.AsNoTracking()
            .Where(r => r.Id == repositoryId)
            .Select(r => r.RulesYaml)
            .FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(yaml))
        {
            return null;
        }
        return ArchitectureRuleService.Parse(yaml);
    }

    private static RuleSetResponse BuildRuleSetResponse(string? yaml)
    {
        IReadOnlyList<RuleResponse> rules = string.IsNullOrWhiteSpace(yaml)
            ? Array.Empty<RuleResponse>()
            : ArchitectureRuleService.Parse(yaml).Rules.Select(ToResponse).ToList();
        return new RuleSetResponse(yaml ?? "", rules);
    }

    private static RuleResponse ToResponse(ArchitectureRule rule) => new(
        rule.Name,
        rule.Severity.ToString().ToLowerInvariant(),
        new RuleConstraintResponse(
            rule.Constraint.Kind.ToString().ToLowerInvariant(),
            ToResponse(rule.Constraint.From),
            rule.Constraint.To is null ? null : ToResponse(rule.Constraint.To)));

    private static RuleSelectorResponse ToResponse(NodeSelector selector) => new(
        selector.PathPrefix,
        selector.Kind?.ToString());

    private static RuleViolationResponse ToResponse(RuleViolation violation) => new(
        violation.RuleName,
        violation.Severity.ToString().ToLowerInvariant(),
        violation.FromKey,
        violation.ToKey,
        violation.FromPath,
        violation.FromLine,
        violation.ToPath,
        violation.ToLine,
        violation.EdgeType?.ToString(),
        violation.Confidence,
        violation.LowConfidence,
        violation.IsMissingRequirement);

    private static RuleDriftEntryResponse ToResponse(RuleDriftEntry entry) => new(
        entry.RuleName,
        entry.Severity.ToString().ToLowerInvariant(),
        entry.FromKey,
        entry.ToKey,
        entry.FromPath,
        entry.ToPath,
        entry.EdgeType?.ToString(),
        entry.IntroducedCommit,
        entry.IsLive,
        entry.LowConfidence);
}

public sealed record RulesRequest(string? Yaml);
