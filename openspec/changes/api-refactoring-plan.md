# Tessera.Api Refactoring Plan

## Goal

Reduce the amount of application logic in `Program.cs`, make endpoint registration and startup behavior consistent, and replace path-based authentication middleware with explicit ASP.NET Core authentication and authorization boundaries.

## Scope

- `src/Tessera.Api/Program.cs`
- `src/Tessera.Api/RepositoryEndpoints.cs` (new)
- `src/Tessera.Api/HealthEndpoints.cs` (new, optional if health remains separate)
- `src/Tessera.Api/TesseraInitializationService.cs` (new)
- `src/Tessera.Api/TesseraAuthenticationHandler.cs` (new)
- `src/Tessera.Api/AuthEndpoints.cs`
- `src/Tessera.Api/GitHubEndpoints.cs`
- `src/Tessera.Api/ReviewEndpoints.cs`
- `src/Tessera.Api/SettingsEndpoints.cs`
- `src/Tessera.Api/AccessControlExtensions.cs` or equivalent authorization helper
- Related integration tests under `tests/Tessera.Integration.Tests`
- Coordinate `AuthEndpoints.cs` token-redirect changes with `web-refactoring-plan.md` decision 1 (cookie-based sessions)

## Design Decisions

### 1. Encapsulate endpoint mappings

Move the inline routes from `Program.cs` into endpoint extension classes:

- `MapHealthEndpoints()` for `/health`.
- `MapRepositoryEndpoints()` for repository listing, details, local creation, snapshots, reprocessing, and cancellation.

Keep the existing endpoint groups for authentication, GitHub, queries, chat, reviews, settings, and rules. `Program.cs` should only compose them:

```csharp
app.MapHealthEndpoints();
app.MapRepositoryEndpoints();
app.MapGitHubEndpoints();
app.MapAuthEndpoints();
app.MapQueryEndpoints();
app.MapChatEndpoints();
app.MapReviewEndpoints();
app.MapSettingsEndpoints();
app.MapRuleEndpoints();
```

Preserve route paths, response contracts, and behavior during this extraction.

### 2. Simplify dependency injection registrations

Use standard constructor injection registration where the container can resolve all dependencies:

```csharp
builder.Services.AddSingleton<IGitHubAppClient, GitHubAppClient>();
```

Retain a factory registration only where construction requires a value or custom setup that is not represented by DI. For `GitHubOAuthClient`, either keep the current factory temporarily or change its constructor to accept `IOptions<GitHubOAuthOptions>` and then register it conventionally.

Consider a typed `HttpClient` registration as a separate improvement. Do not combine that constructor redesign with the initial cleanup unless tests and behavior remain straightforward.

### 3. Isolate startup initialization

Move database migration and AI settings cache refresh into a hosted startup service:

```csharp
builder.Services.AddHostedService<TesseraInitializationService>();
```

The service should create an async scope, resolve `TesseraDbContext` and `AiSettingsCache`, run migrations when `MigrateOnStartup` is enabled, then refresh the cache. Pass the hosted-service cancellation token through all operations.

Do not use an `ApplicationStarted` callback for migrations. That event occurs after the host is considered started and could allow requests before initialization completes.

### 4. Replace path-based authentication middleware

Introduce a custom authentication scheme and handler that:

- Reads the bearer token.
- Accepts the configured dashboard API key.
- Resolves GitHub sessions through `AccessControlService`.
- Produces a `ClaimsPrincipal` and exposes the existing `AccessContext` for resource checks.
- Returns authentication failure without duplicating endpoint-specific authorization logic.

Configure authorization explicitly and protect endpoint groups rather than checking string prefixes in middleware. Keep only intentional public routes outside protected groups:

- `/health` as appropriate for deployment.
- `/api/auth/login`, `/api/auth/callback`, `/api/auth/config`, and `/api/auth/logout` according to their intended behavior.
- `/api/github/webhook`, protected by GitHub signature validation.
- `/api/github/setup` only after adding setup authorization/state validation.

Keep repository access as resource authorization. Authentication answers who the caller is; `GuardRepoAsync` or a dedicated authorization service answers whether that caller can access the specific repository.

Implement and cut over to the new handler in the same deployment rather than running both the old middleware and the new authentication scheme side by side; a dual-auth transition window makes it easy to accidentally leave a route protected by only one of the two mechanisms.

### 5. Make authorization behavior consistent

Resolve the current mismatch where the middleware can allow anonymous requests when authentication is not configured, while `GuardRepoAsync` still returns `401` for missing access.

Choose and document one policy:

- Require authentication consistently for repository routes, including local development; or
- Explicitly support open mode and make repository guards honor it.

The preferred production behavior is explicit authentication for protected repository routes.

### 6. Address related API correctness issues

Include these focused fixes while touching the relevant endpoint surfaces. The GitHub setup endpoint fix and the OAuth token-in-redirect-URL fix are the two highest-severity items from the original review; do not let them get lost among the smaller fixes below.

- Require authorization/state validation for `/api/github/setup` before allowing installation uninstall/import actions (currently unauthenticated and able to disconnect or import repositories).
- Stop redirecting OAuth sessions with `?token=...` in the URL. Coordinate this with the Web plan's cookie-based session migration; if the Web plan lands first, update `AuthEndpoints.RedirectToWeb`/`HandleCallbackAsync` to set the session cookie directly instead of appending a token query parameter.
- Validate `installation_id` with `long.TryParse`; return `400` for malformed or non-positive values.
- Catch `JsonException` for malformed signed webhook bodies and return `400`.
- Never include OAuth provider exception messages in browser redirects. Log diagnostic details server-side and return a generic error plus correlation identifier.
- Derive review `EditedBy` from the authenticated access context instead of accepting it from the request body.
- Replace `AllowAnyOrigin()` with configured frontend origins before production deployment.

## Implementation Sequence

1. Create focused endpoint extension classes and move the inline health/repository mappings from `Program.cs` without changing behavior.
2. Simplify `IGitHubAppClient` registration and add/update DI registration tests if needed.
3. Extract startup migration/cache refresh into `TesseraInitializationService`; verify startup failure behavior and cancellation.
4. Add the authentication handler and wire `AddAuthentication`/`AddAuthorization`.
5. Apply authorization explicitly to endpoint groups and adapt `AccessControlExtensions` to use the authenticated request context.
6. Add the validation and information-disclosure fixes listed above.
7. Remove the path-based middleware and the now-unused `AuthRequired` helper.
8. Update integration tests and documentation/configuration examples.

## Verification

Run from the repository root:

```powershell
dotnet build Tessera.slnx
dotnet test tests/Tessera.Domain.Tests --no-restore
dotnet test tests/Tessera.Integration.Tests --no-restore
```

Add or update tests covering:

- All extracted repository and health routes retain their existing behavior.
- Dashboard API key and GitHub session authentication produce the expected identity.
- Public routes remain public only where intended.
- Unauthenticated and unauthorized repository requests return the correct status codes.
- Startup migration is skipped when `MigrateOnStartup=false` and cache refresh still runs.
- Invalid GitHub installation IDs return `400`.
- Malformed signed webhook JSON returns `400`.
- OAuth failures do not expose provider exception messages.
- Review edits use the authenticated login as `EditedBy`.
- Configured CORS origins are accepted while unconfigured origins are rejected.

## Non-Goals

- Do not redesign domain or infrastructure services as part of this cleanup.
- Do not change public API route names or response shapes unless a security fix requires it.
- Do not move migrations to `ApplicationStarted`.
- Do not introduce a full policy/claims model beyond what is needed to preserve the existing API key, GitHub session, admin, and repository-access behavior.

## Completion Criteria

- `Program.cs` contains service composition, middleware configuration, startup registration, and endpoint composition only.
- Endpoint groups have consistent extension-based registration.
- Startup initialization is isolated and cancellable.
- Authentication is explicit and no longer depends on path-prefix exclusions.
- Repository-level authorization remains enforced.
- The full .NET build and related tests pass.
