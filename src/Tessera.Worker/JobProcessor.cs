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
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Tessera Worker started.");
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

        // Reclaim repos left in an in-progress state by a crashed worker so they
        // are re-analyzed instead of being stuck forever (which also skews the
        // dashboard counters).
        var staleCutoff = DateTimeOffset.UtcNow.AddMinutes(-30);
        var reclaimed = await db.Repositories
            .Where(r => r.IsConnected
                && (r.Status == ProcessingStatus.Cloning
                    || r.Status == ProcessingStatus.Parsing
                    || r.Status == ProcessingStatus.Analyzing
                    || r.Status == ProcessingStatus.Indexing)
                && r.UpdatedAt < staleCutoff)
            .ExecuteUpdateAsync(setters => setters.SetProperty(r => r.Status, ProcessingStatus.Pending), ct);
        if (reclaimed > 0)
        {
            logger.LogWarning("Reclaimed {count} stuck repository job(s) back to Pending.", reclaimed);
        }

        var repo = await db.Repositories
            .Where(r => r.IsConnected && r.Status == ProcessingStatus.Pending)
            .OrderBy(r => r.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (repo is null)
        {
            return;
        }

        if (repo.CancelRequested)
        {
            repo.Status = ProcessingStatus.Cancelled;
            repo.CancelRequested = false;
            repo.StageStartedAt = null;
            repo.ProcessedCount = 0;
            repo.TotalCount = 0;
            repo.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Skipped cancelled repository {repo}", repo.FullName);
            return;
        }

        logger.LogInformation("Processing repository {repo} ({status})", repo.FullName, repo.Status);

        var pipeline = scope.ServiceProvider.GetRequiredService<AnalysisPipeline>();
        await pipeline.ProcessAsync(repo, ct);

        await ProcessPendingPrReviewsAsync(scope, repo, ct);
    }

    private async Task ProcessPendingPrReviewsAsync(IServiceScope scope, Repository repo, CancellationToken ct)
    {
        var db = scope.ServiceProvider.GetRequiredService<TesseraDbContext>();
        var prReviewService = scope.ServiceProvider.GetRequiredService<PrReviewService>();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<AnalysisPipelineOptions>>();
        var workDir = Path.Combine(options.Value.WorkRoot, "repos", repo.FullName);

        var pending = await db.PullRequestReviews.AsNoTracking()
            .Where(r => r.RepositoryId == repo.Id
                && r.HeadSha == repo.LastProcessedCommit
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
}
