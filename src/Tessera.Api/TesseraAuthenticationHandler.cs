using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Tessera.Infrastructure.Auth;

namespace Tessera.Api;

public sealed class TesseraAuthenticationOptions : AuthenticationSchemeOptions;

/// <summary>
/// Authenticates the dashboard API key or a GitHub session token via <see cref="AccessControlService"/>,
/// exposing the resolved <see cref="AccessContext"/> through both the claims principal and
/// <see cref="AccessControlExtensions.ItemsKey"/> for existing resource-level checks (e.g. GuardRepoAsync).
/// </summary>
public sealed class TesseraAuthenticationHandler(
    IOptionsMonitor<TesseraAuthenticationOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    AccessControlService accessService,
    IConfiguration configuration)
    : AuthenticationHandler<TesseraAuthenticationOptions>(options, logger, encoder)
{
    public const string SchemeName = "Tessera";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Context.Request.Headers.Authorization.ToString();
        var access = await accessService.AuthenticateAsync(
            header,
            configuration["Dashboard:ApiKey"] ?? "",
            Context.RequestAborted);

        if (access is null)
        {
            return AuthenticateResult.NoResult();
        }

        Context.Items[AccessControlExtensions.ItemsKey] = access;

        var claims = new List<Claim> { new(ClaimTypes.Name, access.Login) };
        if (access.IsAdmin)
        {
            claims.Add(new Claim(ClaimTypes.Role, "Admin"));
        }

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
        return AuthenticateResult.Success(ticket);
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Response.WriteAsJsonAsync(new { error = "Unauthorized." });
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return Response.WriteAsJsonAsync(new { error = "Forbidden." });
    }
}
