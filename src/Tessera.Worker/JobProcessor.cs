using Microsoft.EntityFrameworkCore;
using Tessera.Domain.Enums;
using Tessera.Infrastructure.Data;
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

        logger.LogInformation("Processing repository {repo} ({status})", repo.FullName, repo.Status);

        var pipeline = scope.ServiceProvider.GetRequiredService<AnalysisPipeline>();
        await pipeline.ProcessAsync(repo, ct);
    }
}
