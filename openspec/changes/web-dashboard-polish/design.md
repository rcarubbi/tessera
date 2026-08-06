## Context

The dashboard (`web/`) is a Next.js 15 App Router client app (React 19, TypeScript strict) styled entirely with a hand-written `globals.css` (GitHub-dark CSS variables + classes like `.card`, `.badge`, `.panel`, `.grid`). Components: `TopBar`, `RepoHub`, `GraphView` (canvas force-layout), `StatusBadge`, `SnapshotSelector`, `DiffView`, `ReviewPanel`, `ChatPanel`, `EntityPanel`. The API is minimal-API in `Tessera.Api` with an `AccessControlExtensions.GuardRepoAsync` auth guard. Worker picks up `IsConnected && Status == Pending` every 5s; no reprocess endpoint exists today (manual SQL was used to reset repos).

## Goals / Non-Goals

**Goals:**
- Adopt Tailwind utility classes for component styling without a visual redesign (keep the dark GitHub-like palette as tokens).
- Surface failures clearly (red) on repo cards and error banners.
- Add a reprocess action per repo backed by a new authed API endpoint.
- Make the graph view navigable: pan/zoom, kind legend, hover highlight, improved force layout.

**Non-Goals:**
- No data-model/migration changes (reprocess reuses existing `Pending` status).
- No new chart/rendering library (keep canvas; avoid adding a React graph lib dependency).
- No changes to worker processing logic.

## Decisions

### D1. Tailwind v4 via PostCSS (no Tailwind config file)
Tailwind v4 is the current major; configured with `@tailwindcss/postcss` and a single `@import "tailwindcss"` in `globals.css`. Automatic content detection removes the need for `tailwind.config.*`. **Alternative considered:** Tailwind v3 with classic config — rejected (v4 is default in 2026, less config, CSS-first theme tokens).
Theme tokens (colors, radius) are declared with `@theme` in `globals.css` mapping to the existing CSS variables, so `bg-[--bg-panel]`-style utilities are unnecessary and current var names remain meaningful.

### D2. Reprocess endpoint semantics
`POST /api/repositories/{id:guid}/reprocess` in `Tessera.Api`:
- Runs `context.GuardRepoAsync(...)` first (404/401/403 same as other endpoints).
- Sets `Status = ProcessingStatus.Pending`, clears `LastProcessedCommit`/`LastSnapshotAt` counters are left as-is (node/edge counts are derived; worker recomputes).
- Returns the updated `Repository` row; idempotent.
**Alternative considered:** reuse push webhook simulation — rejected (webhook is GitHub-signed, not a user action).

### D3. Reprocess button UX
`app/repos/page.tsx` cards become interactive (currently whole card is a `<Link>`). Change to: card body wrapped in `Link`; footer row with status + a `<button>` reprocess action. A small `ReprocessButton` component (client) tracks `idle | loading | done | error` and calls `apiPost`. In-progress shows a spinner + "Requeued" state; failure shows red text; then refreshes the repo list to reflect new status.

### D4. GraphView interaction
Add viewport transform `{scale, tx, ty}` in state; canvas draws in world coordinates and applies `ctx.setTransform`. Wheel zooms around cursor; pointer drag pans; dblclick resets. Hover dims non-neighbors (already exists for selection) and draws an unfilled ring. Legend overlays kind colors. Layout: keep the existing force-directed pass but increase iterations and precompute once per filtered set (already memoized). Labels drawn at node; on hover draw a tooltip chip with symbol+path near cursor.

### D5. globals.css split
Keep tokens + base resets + a few layout primitives (`.container`, `a`) that Tailwind doesn't cover idiomatically; delete component classes (`.card`, `.btn`, `.badge`, `.tabs`, `.panel`, `.grid`, `.muted`, `.list`, `.spinner`) in favor of utilities as components are converted. Component files reference Tailwind classes only.

## Risks / Trade-offs

- **Large class strings in JSX** → acceptable for a small app; consistent with Tailwind idioms. [Risk] vs. design tokens: mitigated by `@theme` tokens so colors stay centralized.
- **Canvas perf on 1000+ node graphs** → existing layout already O(n²); no change in this change; future work may switch to WebGL/SVG virtualization. Pan/zoom is cheap transform-only.
- **Tailwind v4 auto content detection with `path` alias** → ensure `web/` is the root content source (v4 scans project root); validate by `npm run build` that utility classes are emitted.
- **Reprocess races with running job** → worker is single-repo-at-a-time; a repo currently mid-pipeline won't be re-picked while processing (status is not Pending during processing), so double-processing is bounded by the 5s poll window; acceptable for MVP.

## Migration Plan

1. Add Tailwind v4 deps + PostCSS config; verify `npm run dev` renders.
2. Add API endpoint; curl it with `dev-dashboard-key` to confirm 200 + status reset; reprocess is reversible (worker auto-picks).
3. Convert components to Tailwind; keep class names only where needed.
4. Ship: rebuild `web` Docker image (worker unchanged).

## Open Questions

- None blocking. (Optional future: bulk "reprocess all failed" button — out of scope.)
