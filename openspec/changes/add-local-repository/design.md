## Context

Repositories today are created only by the GitHub App flows (`GET /api/github/setup` and the installation/push webhooks in `src/Tessera.Api/GitHubEndpoints.cs`). The worker picks up any repository where `IsConnected && Status == Pending` (`src/Tessera.Worker/JobProcessor.cs`), and the pipeline already treats non-`https://github.com/` clone URLs verbatim (`src/Tessera.Worker/Pipeline/AnalysisPipeline.cs`), which is how the e2e tests run against a local git path. The `Repository` entity has no required GitHub fields: the e2e tests seed `GitHubId = 0` and run the whole pipeline to completion.

Non-admin access is scoped by `InstallationId` (`src/Tessera.Infrastructure/Auth/RepositoryAccess.cs`), so a repository with `InstallationId = 0` would be invisible to everyone but admins. The API serializes `Repository` entities directly (no DTOs), and `FullName` is a unique index (`src/Tessera.Infrastructure/Data/TesseraDbContext.cs`) that also doubles as the clone subdirectory name (`AnalysisPipeline.cs`).

## Goals / Non-Goals

**Goals:**
- Register a local (offline) git repository from the dashboard; no GitHub involved.
- The repository is inactive until the user explicitly runs analysis ("Analyze" / reprocess). No webhook, no push trigger.
- Visible to the user who added it (and admins).
- No docker-compose changes: the user mounts their repo into the worker themselves.

**Non-Goals:**
- No new processing statuses; inactive repos reuse `Pending` + `IsConnected = false`.
- No upload/bundle transfer of local sources into the cluster.
- No removal/disconnect endpoint for local repositories (defer; snapshot FK cascade makes it non-trivial).
- No GitHub webhook/push handling for local repositories.

## Decisions

### D1. Inactive creation: `IsConnected = false`
`POST /api/repositories/local` creates the row with `GitHubId = 0`, `InstallationId = 0`, `Owner = "local"`, `IsConnected = false`, `Status = Pending`. The worker ignores it (its pick-up predicate requires `IsConnected`). The reprocess endpoint sets `IsConnected = true` when it queues a run, which is both the "Analyze" trigger for inactive local repos and harmless for GitHub repos.

### D2. Creator scoping via `Repository.CreatedBy`
Add a `CreatedBy` string column populated with the authenticated `AccessContext.Login` ("admin" for API-key auth, the GitHub login otherwise). `RepositoryAccess.CanAccess`/`Scope` and the list query grant access when `CreatedBy` matches the caller's login (case-insensitive) or the caller is admin. GitHub-registered repos have an empty `CreatedBy`, so their behavior is unchanged.

### D3. Validation: filesystem-safe unique name
`FullName` is both the unique key and the clone folder (`<WorkRoot>/repos/<FullName>`), so the name is validated with `^[A-Za-z0-9._-]{1,100}$` (no path separators or `..`). A duplicate name returns 409. The path must be an absolute container path (starts with `/`, no `..`); existence/validity can only be checked by the worker at clone time, surfacing as `Failed` with an error message.

### D4. Enum binding fix
The web sends reprocess `mode` as a string (`"full"`/`"incremental"`), but minimal API JSON options (`JsonSerializerDefaults.Web`) reject string→enum conversion, so the existing reprocess endpoint returned 400 for any body. `[JsonConverter(typeof(JsonStringEnumConverter))]` on `ReprocessMode` (accepts strings and integers) fixes that for both the existing `ReprocessControls` and the new "Analyze" action without changing `ProcessingStatus` serialization (still numeric, consumed by `StatusBadge`).

### D5. UI surface
The repositories page hosts an "Add local repository" button that expands an inline form (name, worker path, default branch). Local cards show a "local" tag; inactive local cards show an "Analyze →" action that posts a full reprocess. The form follows the existing panel/`field`/`btn` styling and error-feedback conventions.

## Risks / Trade-offs

- **No server-side path validation**: the API container cannot see the worker's filesystem, so a bad path fails at clone time (`Failed` + `ErrorMessage`). Accepted; the path field documents the worker mount requirement.
- **Offline/AI**: local analysis still needs an LLM provider for AI summaries; without one the run is structural-only, consistent with the rest of the system.
- **Creator scoping leaks the login into the entity**: `CreatedBy` is the GitHub login or `admin`. Accepted as the simplest ownership model for a dashboard that already knows the login.

## Migration Plan

1. `Repository.CreatedBy` + DbContext index; `dotnet ef migrations add AddLocalRepositories`.
2. `RepositoryAccess` creator scoping.
3. `Program.cs`: `POST /api/repositories/local`, list filter, reprocess activation.
4. Web: `types.ts`, `AddLocalRepo`, repos page.
5. Tests + verify: build, integration + domain tests, typecheck/build, `openspec validate add-local-repository`, rebuild images, browser check.

## Open Questions

- None blocking.
