## Context

`Repository` is the only state the worker persists about a job (`JobProcessor` opens a fresh scope per job and loads the row from the DB), so a reprocess request must be stored on the row to survive the poll loop. Today `POST /api/repositories/{id}/reprocess` (`Program.cs`) sets `Status = Pending`, clears `LastProcessedCommit` and stage counters, and the pipeline re-analyzes: `LoadPreviousNodesAsync` falls back to the latest snapshot, so unchanged nodes reuse content and only structurally-changed or prompt-outdated nodes hit the LLM.

Summarizer strategies: `AiSummarizer` (prompt `2.1.0`) falls back to `RuleBasedSummarizer` when no provider is configured, the token budget is exhausted, or both providers fail. The fallback records `Model = "rule-based"`, `Confidence = 0.60` — a reliable marker that a node has static-only analysis. Nodes that exist but never produced content get `Content = "// analysis pending"` (`SnapshotComposer.PendingContent`).

## Goals / Non-Goals

**Goals:**
- Two distinct reprocess modes: full (from scratch) and incremental (missing-only).
- Incremental options for static and AI analysis, applied per node only when missing.
- Total processing time shown on the progress screen, frozen at completion.
- Reprocess controls only on the progress screen.

**Non-Goals:**
- No new processing statuses; the existing `ProcessingStatus` enum is unchanged.
- No changes to snapshot retention (one snapshot per commit, unchanged).
- No per-repo persistence of the selected incremental options after the job completes.

## Decisions

### D1. Persist the reprocess request on the `Repository` row
The worker picks up work in a separate scope, so the request must be durable. Add `ReprocessMode` (`Full` default), `IncludeStaticAnalysis`, `IncludeAiAnalysis`. The pipeline reads them at run start and resets them to `Full`/`false` after the run completes so a later push-triggered run is never accidentally incremental.

### D2. Full reprocess means everything from scratch
`LoadPreviousNodesAsync` returns an empty dictionary in `Full` mode, so every parsed node is re-analyzed (AI with rule-based fallback). This changes today's behavior where unchanged nodes reused previous content; the options are ignored in full mode.

### D3. Incremental means missing-only
Incremental keeps `LastProcessedCommit` (so `LoadPreviousNodesAsync` still loads the previous snapshot for reuse) but bypasses the `head == LastProcessedCommit` skip-check. Per node:
- **Missing AI**: no previous node, or `Model` is null/empty/`"rule-based"`, or content is empty/`// analysis pending`, or `StructuralHash` changed, or previous `PromptVersion` differs from the AI summarizer's current prompt version.
- **Missing static**: no previous node, or content is empty/`// analysis pending`, or `StructuralHash` changed.

### D4. Per-node summarizer selection
For each entity that needs work: if `includeAi` and the node is missing AI → `AiSummarizer` (which itself falls back to rule-based on failure); else if `includeStatic` and the node is missing static → `RuleBasedSummarizer`. A node missing both runs AI when AI is selected; otherwise static. One content per node — AI supersedes static.

### D5. Incremental no-op keeps the existing snapshot
If no entity needs the selected analyses, the pipeline skips `PersistAsync`/overview, leaves the existing snapshot untouched (one-per-commit retention), and marks the repo `Completed`. Reprocess with no prior snapshot behaves as "everything is missing".

### D6. Timestamps for total processing time
Add `AnalysisStartedAt` (set once at run start, kept across worker reclamation so the total includes waits) and `CompletedAt` (set on successful completion). Both are cleared by the reprocess endpoint. The progress screen shows elapsed since `AnalysisStartedAt` while running and freezes to `CompletedAt − AnalysisStartedAt` once `Completed`.

### D7. API shape
Extend `POST /api/repositories/{id}/reprocess` with an optional body `{ mode: "full" | "incremental", includeStatic?: boolean, includeAi?: boolean }`. Omitting the body defaults to full (backward compatible). Incremental with neither option set → 400. The endpoint resets `AnalysisStartedAt`, `CompletedAt`, `ErrorMessage`, and stage counters.

### D8. Controls only on the progress screen
`ReprocessButton` is deleted. `web/app/repos/page.tsx` and `web/components/RepoHub.tsx` drop it. A new `ReprocessControls` component on `AnalysisTracker` offers "Reprocess all" and "Reprocess missing"; the latter expands inline options (static / AI checkboxes) and a start action, enabled when at least one option is checked and the repo is not mid-run.

## Risks / Trade-offs

- **Full reprocess cost**: re-analyzing every node re-spends AI budget for the whole repo. That is the explicit meaning of "reprocess everything from scratch"; incremental exists as the cheap path.
- **Timer accuracy across reclamation**: a reclaimed job keeps `AnalysisStartedAt`, so the displayed total includes downtime between attempts. Accepted for simplicity; the worker reclaim threshold (30 min) bounds the skew.
- **Static-only option is often a no-op**: parsing always produces structural data and rule-based content, so `Include static analysis` mainly fills nodes left in `// analysis pending`. Kept for parity with the AI option and for the "no AI budget" case.

## Migration Plan

1. `Repository` fields + `ReprocessMode` enum; `dotnet ef migrations add AddReprocessModesAndTimestamps`.
2. `Program.cs` reprocess endpoint: parse/validate body, set mode/options, clear timestamps.
3. `AnalysisPipeline`: full/incremental branching, per-node static/AI selection, no-op persist, timestamps, flag reset.
4. Web: `types.ts`, `AnalysisTracker` timer + `ReprocessControls`, delete `ReprocessButton`, clean call sites.
5. Verify: build, integration tests, typecheck/build, `openspec validate`, rebuild images, browser check.

## Open Questions

- None blocking.
