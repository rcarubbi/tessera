## 1. OpenSpec artifacts

- [x] 1.1 Create proposal.md
- [x] 1.2 Create specs/preline-foundation/spec.md and specs/web-dashboard/spec.md deltas
- [x] 1.3 Create design.md
- [x] 1.4 Create tasks.md
- [x] 1.5 Validate change with `openspec validate ui-redesign-preline`

## 2. Preline foundation

- [x] 2.1 Install `preline` + `@tailwindcss/forms` in `web/`
- [x] 2.2 Wire Tailwind v4 CSS variants in `globals.css` (`@source` preline dist, `@import` variants.css, `@plugin` forms)
- [x] 2.3 Add `PrelineClient` (dynamic import `preline/non-auto`, `HSStaticMethods.autoInit()` keyed on `usePathname()`, cleanup) and mount in `app/layout.tsx`
- [x] 2.4 Verify `npm run build` emits Preline utilities and `npm run typecheck` passes

## 3. Chrome, layout, repo list

- [x] 3.1 `TopBar` → Preline navbar
- [x] 3.2 `app/repos/page.tsx` repo cards + `StatusBadge` + `ReprocessButton` → Preline card/badge/button
- [x] 3.3 `app/login/page.tsx` restyle

## 4. Repo hub + graph screen

- [x] 4.1 `RepoHub` tabs → Preline tabs (`hs-tabs`)
- [x] 4.2 `GraphView` settings collapsible → Preline accordion; zoom controls → button group (inside canvas top-right); Kinds legend → legend indicator; hover tooltip → Preline tooltip
- [x] 4.3 Graph method/function toggle + edge-type checkboxes → Preline checkbox/switch

## 5. Panels and overlays

- [x] 5.1 `OverviewPanel`, `EntityPanel`, `FilesPanel` → Preline cards
- [x] 5.2 `DiffView` → Preline list/table; `ReviewPanel` → alert/dismiss; `ChatPanel` → chat bubbles + toasts
- [x] 5.3 `DiagramViewer` → Preline modal/overlay (keep zoom-in animation, X/ESC/backdrop close, pan/zoom)
- [x] 5.4 `AnalysisTracker` + progress page, `SnapshotSelector` restyle

## 6. Forms and settings page

- [x] 6.1 `app/settings/page.tsx` inputs/selects → Preline input/select
- [x] 6.2 Global form elements audit (`@tailwindcss/forms` resets)

## 7. CSS cleanup

- [x] 7.1 Delete legacy component classes (`.card`, `.btn`, `.badge`, `.panel`, `.field`, `.spinner`, `.muted`, `.list`, …) from `globals.css`
- [x] 7.2 Keep `@theme` tokens, base resets, and diagram-viewer keyframes

## 8. Verification

- [x] 8.1 `npm run typecheck` + `npm run build`
- [x] 8.2 Browser smoke: all tabs, graph pan/zoom + settings, diagram viewer, chat, reprocess, login/settings
- [x] 8.3 Rebuild `web` Docker image

