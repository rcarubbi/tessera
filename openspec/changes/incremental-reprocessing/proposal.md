## Why

- The progress screen timer keeps counting after analysis completes and only shows the current-stage duration (`StageStartedAt` is overwritten every stage), so there is no way to see the total processing time.
- The only reprocess action re-runs the whole pipeline. There is no way to reprocess just what is missing: filling in static content for nodes without any, or upgrading rule-based nodes to AI analysis without paying for a full re-run.
- Reprocess controls are scattered across the repos list and repo hub; they should live only on the progress screen.

## What Changes

- **Total processing time**: the progress screen freezes the elapsed counter when the pipeline reaches a terminal state (`Completed`) and shows the total time (`CompletedAt − AnalysisStartedAt`).
- **Reprocess modes**: two actions on the progress screen —
  - **Reprocess all**: re-analyzes every node from scratch (full pipeline, AI with rule-based fallback).
  - **Reprocess missing**: incremental; only nodes missing the selected analysis are re-analyzed.
- **Incremental options**: when running "Reprocess missing", the user picks **Include static analysis** and/or **Include AI analysis** (at least one required). Each analysis runs only for nodes that lack it; existing nodes are reused as-is.
- **Incremental no-op**: if nothing is missing, the existing snapshot is kept untouched and the repo is marked completed.
- **Control placement**: reprocess buttons are removed from the repos list and repo hub; they exist only on the progress screen.

## Capabilities

### Modified Capabilities
- `repo-ingestion`: reprocess endpoint accepts a mode plus incremental options; pipeline selects per-node analysis based on what is missing; processing timestamps are persisted.
- `web-dashboard`: progress screen freezes the timer at completion showing total time, and hosts the reprocess controls (full / incremental with static & AI options).

## Impact

- `src/Tessera.Domain/Entities/Repository.cs`: new fields `ReprocessMode`, `IncludeStaticAnalysis`, `IncludeAiAnalysis`, `AnalysisStartedAt`, `CompletedAt`.
- `src/Tessera.Domain/Enums/ReprocessMode.cs` (new): `Full`, `Incremental`.
- `src/Tessera.Infrastructure/Migrations/*`: one migration for the new columns.
- `src/Tessera.Worker/Pipeline/AnalysisPipeline.cs`: mode handling, `previousNodes` by mode, per-node static/AI selection, no-op persist skip, timestamps.
- `src/Tessera.Api/Program.cs`: reprocess endpoint accepts and validates `{ mode, includeStatic, includeAi }`; resets timestamps.
- `web/components/AnalysisTracker.tsx`: frozen total-time stat + reprocess controls.
- `web/components/ReprocessControls.tsx` (new): full / incremental with options.
- `web/components/ReprocessButton.tsx`: removed; call sites in `web/app/repos/page.tsx` and `web/components/RepoHub.tsx` cleaned up.
- `web/lib/types.ts`: Repository shape updated.

## Migration Plan

1. Add domain fields + enum; generate EF migration.
2. Update reprocess endpoint (`Program.cs`) with mode/options body + timestamp reset.
3. Update `AnalysisPipeline`: mode branching, per-node selection, no-op persist, timestamps.
4. Update web: `types.ts`, timer freeze, `ReprocessControls`, remove `ReprocessButton` call sites.
5. Verify: `dotnet build`, integration tests, `npm run typecheck` + `npm run build`, `openspec validate incremental-reprocessing`, rebuild images, browser check.
