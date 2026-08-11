## 1. OpenSpec artifacts

- [x] 1.1 Create proposal.md
- [x] 1.2 Create specs/repo-ingestion/spec.md delta
- [x] 1.3 Create specs/web-dashboard/spec.md delta
- [x] 1.4 Create design.md
- [x] 1.5 Create tasks.md
- [x] 1.6 Validate change with `openspec validate add-local-repository`

## 2. Domain + migration

- [x] 2.1 `Repository.CreatedBy` (string, default `""`)
- [x] 2.2 DbContext config: `HasMaxLength(256)` + index on `CreatedBy`
- [x] 2.3 EF migration `AddLocalRepositories`
- [x] 2.4 `[JsonConverter(typeof(JsonStringEnumConverter))]` on `ReprocessMode` so string `mode` values bind

## 3. Access

- [x] 3.1 `RepositoryAccess.CanAccess`/`Scope`: admin OR installation match OR `CreatedBy` matches login (case-insensitive)

## 4. API

- [x] 4.1 `POST /api/repositories/local`: authenticated, validates name (regex) + path (absolute, no `..`), 409 on duplicate, creates inactive local repo (`GitHubId=0`, `InstallationId=0`, `Owner="local"`, `IsConnected=false`, `Status=Pending`, `CreatedBy`)
- [x] 4.2 List endpoint: non-admin filter includes creator (`r.CreatedBy == access.Login`)
- [x] 4.3 Reprocess sets `IsConnected = true` (manual activation)

## 5. Web

- [x] 5.1 `web/lib/types.ts`: `createdBy` on `Repository`
- [x] 5.2 `web/components/AddLocalRepo.tsx`: name / worker path / default branch form with error feedback
- [x] 5.3 `web/app/repos/page.tsx`: add button, "local" tag, "Analyze →" action on inactive local repos, empty-state text

## 6. Tests

- [x] 6.1 `LocalRepositoryEndpointTests`: 401, 201 inactive, default branch fallback, invalid name/path 400, duplicate 409, any authenticated user, creator visibility, reprocess activation, cross-user 403
- [x] 6.2 `EndToEndPipelineTests`: local offline repo add → activate → pipeline → Completed

## 7. Verification

- [x] 7.1 `dotnet build Tessera.slnx`
- [x] 7.2 Integration tests + domain tests green
- [x] 7.3 `npm run typecheck` + `npm run build`
- [x] 7.4 `openspec validate add-local-repository`
- [ ] 7.5 Rebuild images; browser check (add local repo, analyze, reprocess)
