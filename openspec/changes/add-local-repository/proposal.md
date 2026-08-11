## Why

- Repositories can only be registered through the GitHub App install/webhook flow. Analyzing a local git repository (offline, on the same machine as the stack) currently requires manual SQL inserts plus a worker-visible clone path.
- Users want to add a local repository whose analysis runs **only manually** — no webhook, no push trigger.

## What Changes

- **Add local repository**: any signed-in user registers a local git repository by name and an absolute path visible inside the worker container. It is created **inactive** (`IsConnected = false`, `Status = Pending`) so the worker never picks it up on its own.
- **Manual activation**: an inactive local repository shows an "Analyze" action that queues a full run; the existing reprocess actions re-run it later. Local repositories have no webhook or push trigger.
- **Creator scoping**: local repositories are visible only to the user who added them and to admins, via a new `Repository.CreatedBy` column. `GitHubId`, `InstallationId` and `Owner` are `0`/`local`.
- **No infra change**: the worker already clones non-GitHub URLs verbatim, so the user mounts their repo into the worker themselves and supplies the container path.

## Capabilities

### Modified Capabilities
- `repo-ingestion`: repositories can be registered directly as local (offline) repositories; they are inactive until manually activated and scoped to the creating user.
- `web-dashboard`: the repositories list gains an "Add local repository" form, a "local" tag, and an "Analyze" action on inactive local repositories.

## Impact

- `src/Tessera.Domain/Entities/Repository.cs`: new `CreatedBy` column.
- `src/Tessera.Infrastructure/Data/TesseraDbContext.cs`: `CreatedBy` index + max length.
- `src/Tessera.Infrastructure/Migrations/*`: `AddLocalRepositories` migration.
- `src/Tessera.Infrastructure/Auth/RepositoryAccess.cs`: creator (or admin) access.
- `src/Tessera.Api/Program.cs`: `POST /api/repositories/local` (create inactive local repo), list filter includes creator, reprocess activates inactive repos.
- `src/Tessera.Domain/Enums/ReprocessMode.cs`: `JsonStringEnumConverter` so string `mode` values bind (fixes the existing web reprocess call path).
- `web/lib/types.ts`: `createdBy` on `Repository`.
- `web/components/AddLocalRepo.tsx` (new): add-local form.
- `web/app/repos/page.tsx`: add button, local tag, "Analyze" action.
- `tests/Tessera.Integration.Tests/LocalRepositoryEndpointTests.cs` (new): endpoint + access tests.
- `tests/Tessera.Integration.Tests/EndToEndPipelineTests.cs`: local-repo add → activate → pipeline → completed.

## Migration Plan

1. Add `CreatedBy` field + DbContext config; generate `AddLocalRepositories` migration.
2. Update `RepositoryAccess` for creator scoping.
3. Add `POST /api/repositories/local` + list filter + reprocess activation in `Program.cs`.
4. Web: `types.ts`, `AddLocalRepo`, repos page.
5. Tests + verify: `dotnet build`, integration + domain tests, `npm run typecheck` + `npm run build`, `openspec validate add-local-repository`, rebuild images, browser check.
