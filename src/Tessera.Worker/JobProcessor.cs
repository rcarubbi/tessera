using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tessera.Domain.Entities;
using Tessera.Domain.Enums;
using Tessera.Infrastructure.Data;
using Tessera.Infrastructure.Reviews;
using Tessera.Worker.Pipeline;

namespace Tessera.Worker;

public sealed class JobProcessor(
    IServiceScopeFactory scopeFactory,
    ILogger<JobProcessor> logger) : BackgroundService
{
    private readonly string _workerInstanceId = Guid.NewGuid().ToString("N");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Tessera Worker started (instance {instance}).", _workerInstanceId);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Job loop error");
            }
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    private async Task ProcessPendingAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TesseraDbContext>();
        var leaseDuration = scope.ServiceProvider.GetRequiredService<IOptions<AnalysisPipelineOptions>>().Value.LeaseDuration;

        var repo = await ClaimNextRepositoryAsync(db, leaseDuration, ct);
        if (repo is null)
        {
            await ProcessIdlePrReviewsAsync(scope, ct);
            return;
        }

        if (repo.CancelRequested)
        {
            repo.Status = ProcessingStatus.Cancelled;
            repo.CancelRequested = false;
            repo.StageStartedAt = null;
            repo.ProcessedCount = 0;
            repo.TotalCount = 0;
            repo.ProcessingLeaseId = null;
            repo.LeaseExpiresAt = null;
            repo.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Skipped cancelled repository {repo}", repo.FullName);
            return;
        }

        logger.LogInformation("Processing repository {repo} ({status})", repo.FullName, repo.Status);

        var pipeline = scope.ServiceProvider.GetRequiredService<AnalysisPipeline>();
        var result = await pipeline.ProcessAsync(repo, ct);

        if (result == PipelineResult.Completed && repo.Status == ProcessingStatus.Completed)
        {
            await ProcessPendingPrReviewsAsync(scope, repo, ct);
        }
    }

    // Claims a repository by atomically flipping Pending -> Cloning (or reclaiming an expired lease) with a
    // fresh lease id/expiration. ExecuteUpdateAsync's affected-row count is the only proof of ownership: a
    // count of 0 means another worker (or nothing) won the race, so this instance must not touch the row.
    public async Task<Repository?> ClaimNextRepositoryAsync(TesseraDbContext db, TimeSpan leaseDuration, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var candidateId = await db.Repositories
            .Where(r => r.IsConnected
                && (r.Status == ProcessingStatus.Pending
                    || ((r.Status == ProcessingStatus.Cloning || r.Status == ProcessingStatus.Parsing || r.Status == ProcessingStatus.Analyzing || r.Status == ProcessingStatus.Indexing)
                        && r.LeaseExpiresAt != null && r.LeaseExpiresAt < now)))
            .OrderBy(r => r.CreatedAt)
            .Select(r => r.Id)
            .FirstOrDefaultAsync(ct);

        if (candidateId == Guid.Empty)
        {
            return null;
        }

        var leaseId = Guid.NewGuid();
        var leaseExpiresAt = now.Add(leaseDuration);

        var claimed = await db.Repositories
            .Where(r => r.Id == candidateId
                && r.IsConnected
                && (r.Status == ProcessingStatus.Pending
                    || ((r.Status == ProcessingStatus.Cloning || r.Status == ProcessingStatus.Parsing || r.Status == ProcessingStatus.Analyzing || r.Status == ProcessingStatus.Indexing)
                        && r.LeaseExpiresAt != null && r.LeaseExpiresAt < now)))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(r => r.Status, ProcessingStatus.Cloning)
                .SetProperty(r => r.ProcessingLeaseId, leaseId)
                .SetProperty(r => r.LeaseExpiresAt, leaseExpiresAt)
                .SetProperty(r => r.WorkerInstanceId, _workerInstanceId)
                .SetProperty(r => r.StageStartedAt, now)
                .SetProperty(r => r.ProcessedCount, 0)
                .SetProperty(r => r.TotalCount, 0)
                .SetProperty(r => r.UpdatedAt, now),
                ct);

        if (claimed != 1)
        {
            // Another worker (or a concurrent poll from this one) claimed it first; skip this cycle.
            return null;
        }

        // ExecuteUpdateAsync bypasses the change tracker, so if this context already tracked the entity
        // (e.g. from a prior query), a plain query would hand back the stale, pre-claim in-memory values.
        // Reload forces a fresh SELECT into the tracked entry.
        var entity = await db.Repositories.FindAsync([candidateId], ct);
        if (entity is not null)
        {
            await db.Entry(entity).ReloadAsync(ct);
        }
        return entity;
    }

    private async Task ProcessPendingPrReviewsAsync(IServiceScope scope, Repository repo, CancellationToken ct)
    {
        var db = scope.ServiceProvider.GetRequiredService<TesseraDbContext>();
        var prReviewService = scope.ServiceProvider.GetRequiredService<PrReviewService>();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<AnalysisPipelineOptions>>();
        var workDir = Path.Combine(options.Value.WorkRoot, "repos", repo.FullName);

        var pending = await db.PullRequestReviews.AsNoTracking()
            .Where(r => r.RepositoryId == repo.Id
                && (r.Status == PrReviewStatus.Queued || r.Status == PrReviewStatus.Failed))
            .ToListAsync(ct);

        foreach (var review in pending)
        {
            var tracked = await db.PullRequestReviews.FirstAsync(r => r.Id == review.Id, ct);
            if (tracked.Status is not (PrReviewStatus.Queued or PrReviewStatus.Failed))
            {
                continue;
            }
            await prReviewService.ProcessAsync(repo, tracked, workDir, ct);
        }
    }

    // Processes queued/failed PR reviews for idle repositories (no pending analysis). Reviews now run
    // against the latest analyzed snapshot, so they no longer depend on a freshly completed pipeline.
    private async Task ProcessIdlePrReviewsAsync(IServiceScope scope, CancellationToken ct)
    {
        var db = scope.ServiceProvider.GetRequiredService<TesseraDbContext>();
        var repoId = await db.PullRequestReviews.AsNoTracking()
            .Where(r => r.Status == PrReviewStatus.Queued || r.Status == PrReviewStatus.Failed)
            .Where(r => db.Snapshots.Any(s => s.RepositoryId == r.RepositoryId))
            .GroupBy(r => r.RepositoryId)
            .OrderBy(g => g.Min(r => r.UpdatedAt))
            .Select(g => g.Key)
            .FirstOrDefaultAsync(ct);
        if (repoId == Guid.Empty)
        {
            return;
        }

        var repo = await db.Repositories.FirstOrDefaultAsync(r => r.Id == repoId, ct);
        if (repo is null)
        {
            return;
        }

        try
        {
            await ProcessPendingPrReviewsAsync(scope, repo, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Idle PR review processing failed for {repo}", repo.FullName);
        }
    }
}

