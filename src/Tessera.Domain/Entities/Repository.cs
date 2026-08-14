using Tessera.Domain.Enums;

namespace Tessera.Domain.Entities;

public class Repository
{
    public Guid Id { get; set; }
    public long GitHubId { get; set; }
    public string Owner { get; set; } = "";
    public string Name { get; set; } = "";
    public string FullName { get; set; } = "";
    public string DefaultBranch { get; set; } = "main";
    public string? CloneUrl { get; set; }
    public long InstallationId { get; set; }
    public string CreatedBy { get; set; } = "";
    public bool IsConnected { get; set; } = true;
    public ProcessingStatus Status { get; set; } = ProcessingStatus.Pending;
    public string? LastProcessedCommit { get; set; }
    public DateTimeOffset? LastSnapshotAt { get; set; }
    public int NodeCount { get; set; }
    public int EdgeCount { get; set; }
    public DateTimeOffset? StageStartedAt { get; set; }
    public int ProcessedCount { get; set; }
    public int TotalCount { get; set; }
    public string? ErrorMessage { get; set; }
    public bool CancelRequested { get; set; }
    public Guid? ProcessingLeaseId { get; set; }
    public DateTimeOffset? LeaseExpiresAt { get; set; }
    public string? WorkerInstanceId { get; set; }
    public ReprocessMode ReprocessMode { get; set; } = ReprocessMode.Full;
    public bool IncludeStaticAnalysis { get; set; }
    public bool IncludeAiAnalysis { get; set; }
    public bool IncludeIndexing { get; set; }
    public string? RulesYaml { get; set; }
    public bool EnablePrComments { get; set; } = true;
    public DateTimeOffset? AnalysisStartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
