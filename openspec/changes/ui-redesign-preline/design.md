## Context

The dashboard is a Next.js 15 App Router client app (React 19, TypeScript strict) on Tailwind v4 (`@tailwindcss/postcss`, CSS-first `@theme` tokens in `globals.css`). Styling is a bespoke set of classes defined in `globals.css` (`.card`, `.panel`, `.btn`, `.btn-*`, `.badge`, `.badge-*`, `.field`, `.muted`, `.list`, `.spinner`, `.path`, `.markdown`, `.citation-chip`, `.link-button`, `.cycle-banner`). Interactions are hand-rolled React: tab switching in `RepoHub` (`useState<Tab>`), the collapsible graph settings + in-canvas overlays in `GraphView`, the `DiagramViewer` overlay (fixed `z-50` div), chat UI, and hover tooltips. `reagraph` renders the graph canvas with its own pan/zoom. Recent changes (`diagram-viewer`, `graph-controls`) added the diagram viewer and collapsible settings that this redesign must preserve.

Preline (preline.co, v4.2) is a Tailwind component library distributed via npm (`preline`) with CSS variants for Tailwind v4 (`variants.css`, `@source` for `dist/*.js`) and DOM-driven JS plugins (modal/overlay, accordion, tabs, dropdown, tooltip, switch, select, chat bubbles, toasts). The official Next.js guide wires it through a client-only loader using `preline/non-auto` + `HSStaticMethods.autoInit()` keyed on `usePathname()`.

## Goals / Non-Goals

**Goals:**
- Adopt Preline as the component vocabulary across all dashboard surfaces.
- Replace hand-written interaction code (tabs, collapsible settings, modal viewer, tooltips) with Preline plugins where it reduces code, while preserving behavior.
- Remove the bespoke component classes from `globals.css` (keep theme tokens + base resets).
- Preserve the existing dark GitHub-like palette via the current `@theme` tokens.

**Non-Goals:**
- No new page/navigation structure (tabs layout stays).
- No changes to the `reagraph` graph canvas internals or the graph API.
- No backend/worker/DB/analyzer changes.
- No Preline Theme switching or dark/light toggle feature.
- No migration of the graph, diagram, or analysis pipeline logic.

## Decisions

### D1. Preline install + Tailwind v4 wiring
Add `preline` (runtime) and `@tailwindcss/forms` (dev) to `web/`. In `globals.css`, after `@import "tailwindcss"`:
- `@source "./node_modules/preline/dist/*.js";` — so Tailwind scans Preline class names.
- `@import "./node_modules/preline/variants.css";` — Preline CSS variants.
- `@plugin "@tailwindcss/forms";` — form element resets Preline forms rely on.
Verified by `npm run build` emitting Preline utilities. **Alternative considered:** Preline CDN/static script — rejected (npm is the project convention and keeps versioning/build deterministic).

### D2. `PrelineClient` lifecycle per official Next.js guide
`"use client"` component that dynamic-imports `preline/non-auto` inside `useEffect` (browser-only, no SSR DOM access) and calls `HSStaticMethods.autoInit()`. The effect dependency is `usePathname()`, so every App Router navigation rescans the new route DOM; `autoInit` is collection-aware (drops stale nodes, skips initialized ones). A `cancelled` flag prevents setting state/scanning after unmount. Mounted once at the end of `app/layout.tsx` `<body>` so committed route markup exists before the scan. Components that own a single plugin root (e.g. a dropdown) may use manual `new HSXxx(root)` + `destroy()` instances instead of relying on the global scan.

### D3. Component mapping (Preline surface ↔ current surface)
| Current | Preline |
|---|---|
| TopBar | navbar |
| repo cards / badges / status | card / badge / button |
| RepoHub tabs | tabs (`hs-tabs`) |
| graph settings collapsible | accordion |
| graph zoom controls | button group |
| Kinds legend / hover tooltip | legend indicator / tooltip |
| DiagramViewer overlay | modal / overlay |
| ChatPanel | chat bubbles + toasts |
| ReviewPanel | alert / dismiss |
| forms (settings page, selects, checkboxes) | input / select / checkbox / switch |

### D4. globals.css cleanup
Delete the bespoke component classes after every surface is converted; keep `@theme` tokens, base resets (`*`, `body`, `code`, `pre`), and the `@keyframes` zoom-in used by the diagram viewer. Convert remaining presentational rules (`.markdown`, `.path`, `.citation-chip`) to utilities/components.

### D5. Graph canvas untouched
Preline has no graph primitives; `reagraph` stays the canvas renderer with its own pan/zoom. Only the surrounding overlays (tooltip, legend, controls, footer) get Preline styling.

## Risks / Trade-offs

- **Preline JS vs React hydration double-binding** — Preline is DOM-driven and idempotent (`autoInit` skips initialized nodes); risk contained by following the collection-aware scan pattern; per-component manual instances with `destroy()` where a component owns the root. [Risk] → mitigate in D2.
- **Tailwind v4 `@source`/`variants.css` interplay** — ordering of `@import`/`@source`/`@plugin` in `globals.css` is order-sensitive; verify generated CSS contains Preline classes after build.
- **`@tailwindcss/forms` resets** — may alter existing custom form look; audit inputs/selects during the forms step.
- **Large refactor regression surface** — web has no unit tests; mitigated by keeping behavior identical, `npm run typecheck` + `npm run build`, and a manual browser smoke pass across every tab.
- **Custom interactions that Preline doesn't cover** — diagram pan/zoom and graph pan/zoom stay hand-rolled; only their chrome (buttons, modal, tooltip) is Preline.

## Migration Plan

1. Install deps; wire Tailwind v4 CSS variants; confirm build emits Preline utilities.
2. `PrelineClient` + layout mount; verify hydration + navigation rescan.
3. Restyle in dependency order (see proposal Impact): chrome/layout → repo list → hub tabs → graph screen → panels → viewer modal → forms.
4. Delete legacy classes from `globals.css`; keep tokens/resets/keyframes.
5. Verify: `npm run typecheck`, `npm run build`, browser smoke (all tabs + graph + viewer + chat + reprocess), rebuild web Docker image.

## Open Questions

- Whether to adopt a Preline Theme (out of scope; current tokens preserved).
- Optional future: persist per-repo graph settings (from `graph-controls`) in `localStorage`.
