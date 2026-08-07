## 1. OpenSpec artifacts

- [x] 1.1 Create proposal.md
- [x] 1.2 Create specs/repo-ingestion/spec.md delta
- [x] 1.3 Create specs/web-dashboard/spec.md delta
- [x] 1.4 Create design.md
- [x] 1.5 Create tasks.md
- [x] 1.6 Validate change with `openspec validate incremental-reprocessing`

## 2. Domain + migration

- [x] 2.1 `ReprocessMode` enum (`Full`, `Incremental`) in `src/Tessera.Domain/Enums`
- [x] 2.2 `Repository` fields: `ReprocessMode`, `IncludeStaticAnalysis`, `IncludeAiAnalysis`, `AnalysisStartedAt`, `CompletedAt`
- [x] 2.3 EF migration `AddReprocessModesAndTimestamps`

## 3. Worker pipeline

- [x] 3.1 `AnalysisPipeline` injects `RuleBasedSummarizer` alongside the AI summarizer
- [x] 3.2 Skip-check bypassed when `ReprocessMode == Incremental`; `AnalysisStartedAt` set at run start
- [x] 3.3 `LoadPreviousNodesAsync` returns empty for full reprocess (everything from scratch)
- [x] 3.4 Per-node missing detection: missing AI (Model null/rule-based, pending content, structural change, prompt version) and missing static (no content, pending, structural change)
- [x] 3.5 Summarizer selection: AI when selected & missing; otherwise static when selected & missing
- [x] 3.6 Incremental no-op: nothing missing → skip persist/overview, keep snapshot, mark `Completed`
- [x] 3.7 `CompletedAt` set on success; mode/options reset to defaults after the run

## 4. API

- [x] 4.1 Reprocess endpoint accepts body `{ mode, includeStatic, includeAi }`, defaults to full
- [x] 4.2 Validate incremental requires at least one option (400 otherwise)
- [x] 4.3 Endpoint persists mode/options and clears timestamps + error

## 5. Web

- [x] 5.1 `web/lib/types.ts`: add `analysisStartedAt`, `completedAt` (and mode fields if surfaced)
- [x] 5.2 `AnalysisTracker`: freeze timer at terminal, show total (`completedAt - analysisStartedAt`)
- [x] 5.3 `ReprocessControls` component: "Reprocess all" + "Reprocess missing" with static/AI checkboxes; disabled while running
- [x] 5.4 Delete `ReprocessButton.tsx`; remove usage from `web/app/repos/page.tsx` and `web/components/RepoHub.tsx`
- [x] 5.5 Wire `ReprocessControls` into the failed-state card too

## 6. Verification

- [x] 6.1 `dotnet build Tessera.slnx` + integration tests
- [x] 6.2 `npm run typecheck` + `npm run build`
- [x] 6.3 `openspec validate incremental-reprocessing`
- [x] 6.4 Rebuild images; browser check on progress screen (timer freeze + both reprocess paths)
