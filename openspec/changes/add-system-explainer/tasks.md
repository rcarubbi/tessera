## 1. OpenSpec artifacts

- [x] 1.1 Create proposal.md
- [x] 1.2 Create specs/system-explainer/spec.md
- [x] 1.3 Create specs/web-dashboard/spec.md delta
- [x] 1.4 Create design.md
- [x] 1.5 Create tasks.md
- [x] 1.6 Validate change with `openspec validate add-system-explainer`

## 2. Explainer service

- [x] 2.1 Records: `ExplainResult` (summary, mainComponents, architecturalNotes, externalSystems, criticalComponents, diagram?), `ExplainedComponent` (key, symbol, path, line, kind, role), `CriticalComponent` (key, symbol, centrality)
- [x] 2.2 `ExplainerService.BuildAsync(repoId, commitSha?, ct)`: use stored overview if present, else `OverviewService.GenerateAsync`
- [x] 2.3 Lenient markdown section parser (`## Summary`, `## Main components`, `## Architectural notes`, optional `## External systems`); resolve `[key]` claims against node map, drop unresolvable
- [x] 2.4 `GraphQueryService.TopByDegreeAsync(repoId, commitSha?, top=10)`: in+out degree ordering

## 3. API

- [x] 3.1 `GET /api/repositories/{id}/explain` with access guard; empty-state for no snapshot

## 4. Web

- [x] 4.1 `web/lib/types.ts`: explain types
- [x] 4.2 `ExplainerView.tsx` (new): steps Summary / Critical components / Explore with clickable claims → entity detail; component diagram via `Mermaid`
- [x] 4.3 Repo hub entry point ("Explain this system")

## 5. Tests

- [x] 5.1 Section parsing + claim resolution + unresolvable dropped
- [x] 5.2 Critical components centrality ordering
- [x] 5.3 Rule-based fallback shape; empty snapshot empty-state
- [x] 5.4 403 without access

## 6. Verification

- [x] 6.1 `dotnet build Tessera.slnx`
- [x] 6.2 Integration + domain tests green
- [x] 6.3 `npm run typecheck` + `npm run build`
- [x] 6.4 `openspec validate add-system-explainer`
- [ ] 6.5 Rebuild images; browser check
