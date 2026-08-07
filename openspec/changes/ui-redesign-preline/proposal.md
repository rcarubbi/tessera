## Why

The dashboard is styled with a hand-rolled set of Tailwind utility classes (`globals.css` `.card`, `.btn`, `.badge`, `.panel`, `.field`, `.spinner`, `.muted`, `.list`, …) plus ad-hoc React state for every interaction: tab switching in `RepoHub`, the collapsible settings area and in-canvas overlays in `GraphView`, the diagram viewer overlay, chat, and tooltips. There is no shared component vocabulary, no accessible primitives (modal, tooltip, accordion, switch), and no consistent spacing/state styling. Preline (preline.co) is an open-source Tailwind CSS component library with tested JS plugins (modal/overlay, accordion, tabs, dropdown, tooltip, switch, select, cards, badges, chat bubbles) that matches the existing dark GitHub-like theme and replaces the hand-written CSS and bespoke interaction code.

## What Changes

- **Integrate Preline** into the Next.js app:
  - Add `preline` and `@tailwindcss/forms` to `web/package.json`.
  - Wire the Tailwind v4 CSS variants in `globals.css`: `@source "./node_modules/preline/dist/*.js"`, `@import "./node_modules/preline/variants.css"`, `@plugin "@tailwindcss/forms"`.
  - Add a `PrelineClient` client component per the official Next.js guide: dynamic import of `preline/non-auto`, `HSStaticMethods.autoInit()` on mount and after every `usePathname()` change, with unmount cleanup. Mount it at the end of the root layout `<body>` so route content exists before the scan; `autoInit` is collection-aware (filters stale nodes, skips already-initialized ones).
- **Restyle every surface** with Preline components while preserving the current behavior:
  - `TopBar` → Preline navbar; repo cards, `StatusBadge`, buttons/badges → Preline card/badge/button classes.
  - `RepoHub` tabs → Preline tabs (`hs-tabs`); the graph settings collapsible area → Preline accordion; graph zoom/controls → Preline button group.
  - `DiagramViewer` overlay → Preline modal/overlay (retains zoom-in animation, X/ESC/backdrop close, pan/zoom).
  - `GraphView` overlays → Preline tooltip and legend-indicator components; reagraph canvas itself is unchanged.
  - `ChatPanel` → Preline chat bubbles + toasts; `ReviewPanel` → alert/dismiss patterns; `DiffView` → list/table; `EntityPanel`/`OverviewPanel` → Preline cards.
  - Forms (settings page, selects, checkboxes, the graph settings toggles) → Preline input/select/checkbox/switch.
- **Remove hand-written component classes** from `globals.css` (`.card`, `.btn`, `.badge`, `.panel`, `.field`, `.muted`, `.list`, `.spinner`, …) in favor of Preline/utility classes; keep the `@theme` color tokens and base resets.

## Capabilities

### New Capabilities
- `preline-foundation`: Preline UI installed, wired into Tailwind v4, and initialized client-side in the Next.js App Router lifecycle (mount + route navigation rescan + cleanup).

### Modified Capabilities
- `web-dashboard`: all dashboard surfaces (topbar, repo cards, repo hub tabs, graph screen controls/overlays, panels, chat/review/diff, forms, diagram viewer overlay) are restyled with Preline components; interactive states run through Preline JS plugins. This builds on and preserves the behaviors specified by `diagram-viewer` and `graph-controls`.

## Impact

- `web/package.json` + `web/package-lock.json`: `preline`, `@tailwindcss/forms`.
- `web/app/globals.css`: Preline CSS variants; legacy component classes removed; theme tokens retained.
- `web/components/PrelineClient.tsx` (new): Preline init/rescan lifecycle.
- `web/app/layout.tsx`: mount `PrelineClient`.
- `web/components/*` and `web/app/**/page.tsx`: Preline-based markup restyle (no behavior change).
- No API, worker, database, or analyzer changes. No reagraph changes (graph canvas remains custom).

## Migration Plan

1. Install `preline` + `@tailwindcss/forms`; wire Tailwind v4 CSS variants; verify `npm run build` emits Preline utilities.
2. Add `PrelineClient` and mount it in the root layout; verify plugins initialize after hydration and after navigation.
3. Restyle surfaces in dependency order: TopBar/layout/login → repo cards/status → RepoHub tabs → graph screen (accordion, button group, tooltip, legend) → panels (overview/entity/diff/review/chat) → diagram viewer modal → forms/settings.
4. Delete legacy component classes from `globals.css`.
5. `npm run typecheck` + `npm run build`; browser smoke test all tabs and interactions; rebuild the `web` Docker image.
