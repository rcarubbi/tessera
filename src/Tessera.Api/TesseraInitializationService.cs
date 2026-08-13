using Microsoft.EntityFrameworkCore;
using Tessera.Infrastructure.Ai;
using Tessera.Infrastructure.Data;

namespace Tessera.Api;

/// <summary>
/// Runs database migration and AI settings cache warm-up before the host accepts requests.
/// </summary>
public sealed class TesseraInitializationService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();

        var db = scope.ServiceProvider.GetRequiredService<TesseraDbContext>();
        if (configuration.GetValue<bool>("MigrateOnStartup", true))
        {
            await db.Database.MigrateAsync(cancellationToken);
        }

        await scope.ServiceProvider
            .GetRequiredService<AiSettingsCache>()
            .RefreshAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
