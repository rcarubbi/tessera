## Why

The dashboard lists 168+ connected repositories and renders a raw canvas graph, but the UI is generic (custom CSS, flat cards, no feedback actions). Failed repositories are indistinguishable from pending ones at a glance, there is no way to re-queue a failed/outdated repository, and the graph view offers no pan/zoom or legend. Users lose trust in the pipeline because errors are silent and re-processing requires manual SQL.

## What Changes

- Style the web dashboard with **Tailwind CSS** utility classes (dark GitHub-like theme preserved) across TopBar, repo cards, tabs, badges, buttons, and panels.
- Make **error states explicit**: `Failed` status and API errors render in red with clear visual treatment on repo cards.
- Add a **reprocess button** to each repository card (and repo hub header) that re-queues the repository for the worker (sets `Status=Pending`, clears `LastProcessedCommit`) with loading/feedback state.
- Add **`POST /api/repositories/{id}/reprocess`** (auth-guarded, scoped to the user's installations) to the API.
- **Improve the graph view**: pan and zoom controls, node kind legend, better force-directed layout, hover labels, and loading/empty/error states that match the new styling.
- Refactor `globals.css`: keep only theme tokens and a few base styles; component styling moves to Tailwind utilities.

## Capabilities

### New Capabilities
<!-- None - no new capability boundaries; behavior lands inside the existing web-dashboard spec. -->

### Modified Capabilities
- `web-dashboard`: Repository dashboard gains reprocess action + explicit error states; graph view gains pan/zoom, legend, and improved interaction; dashboard adopts Tailwind styling.

## Impact

- `web/` Next.js app: add Tailwind v4 + PostCSS; restyle `TopBar`, `StatusBadge`, `RepoHub`, `GraphView`, `app/repos/page.tsx`; convert `globals.css`.
- `src/Tessera.Api/Program.cs`: new reprocess endpoint using existing `GuardRepoAsync`/`AccessControlExtensions`.
- Worker unchanged: it already picks up `IsConnected && Status == Pending`.
- No schema/migration changes.
