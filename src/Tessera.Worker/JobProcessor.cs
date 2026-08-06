using Microsoft.EntityFrameworkCore;
using Tessera.Domain.Enums;
using Tessera.Infrastructure.Data;
using Tessera.Worker.Pipeline;

namespace Tessera.Worker;

public sealed class JobProcessor(
    TesseraDbContext db,
    AnalysisPipeline pipeline,
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
        var repo = await db.Repositories
            .Where(r => r.IsConnected && r.Status == ProcessingStatus.Pending)
            .OrderBy(r => r.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (repo is null)
        {
            return;
        }

        logger.LogInformation("Processing repository {repo} ({status})", repo.FullName, repo.Status);
        await pipeline.ProcessAsync(repo, ct);
    }
}
