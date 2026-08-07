using Tessera.Infrastructure.Ai;
using Tessera.Infrastructure.Auth;

namespace Tessera.Api;

public static class SettingsEndpoints
{
    public static void MapSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/settings/ai", async (
            HttpContext context,
            AiSettingsService settings,
            CancellationToken ct) =>
        {
            var access = context.GetAccess();
            if (access is null)
            {
                return Results.Unauthorized();
            }
            return Results.Ok(await settings.GetAsync(ct));
        });

        app.MapPut("/api/settings/ai", async (
            HttpContext context,
            AiSettingsService settings,
            AiSettingsRequest request,
            CancellationToken ct) =>
        {
            var access = context.GetAccess();
            if (access is null)
            {
                return Results.Unauthorized();
            }
            if (!access.IsAdmin)
            {
                return Results.Json(new { error = "Only administrators can change AI settings." }, statusCode: 403);
            }
            try
            {
                return Results.Ok(await settings.SaveAsync(request, ct));
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });
    }
}
