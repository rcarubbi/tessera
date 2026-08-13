using Microsoft.EntityFrameworkCore;
using Tessera.Infrastructure.Data;

namespace Tessera.Api;

public static class HealthEndpoints
{
    public static void MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health", async (TesseraDbContext db) =>
        {
            var canConnect = await db.Database.CanConnectAsync();
            return Results.Ok(new { status = canConnect ? "ok" : "degraded" });
        });
    }
}
