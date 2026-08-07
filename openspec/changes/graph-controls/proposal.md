## Why

The graph screen's top toolbar packs the module filter, edge-type checkboxes, hop-depth select, and zoom buttons into one crowded row, while the zoom controls sit far from the canvas they act on. Large graphs are also dominated by hundreds of `Method`/`Function` nodes that bury the structural shapes (classes, interfaces, controllers), with no way to hide them.

## What Changes

- Move the **module filter**, **edge-type checkboxes**, and **Expand (hop depth)** select into a **collapsible "Settings" area** above the graph canvas (collapsed by default, chevron toggle).
- Move the **zoom controls (+ / − / Reset)** **inside the canvas** as an overlay in the **top-right corner**, mirroring the existing Kinds legend at the top-left.
- Add a **"Show method nodes" toggle** (default **on**) that, when disabled, removes nodes of kind `Method` and `Function` from the graph along with edges that become orphaned.

## Capabilities

### New Capabilities
<!-- None - no new capability boundary; behavior lands inside the existing web-dashboard spec. -->

### Modified Capabilities
- `web-dashboard`: graph controls are reorganized into a collapsible settings area with in-canvas zoom controls; the graph gains a method/function node visibility toggle.

## Impact

- `web/components/GraphView.tsx` only: toolbar → collapsible settings; zoom overlay relocated; `visibleNodes`/edge filtering extended with the method toggle.
- No API, worker, database, or other web component changes.

## Migration Plan

1. Extract the top toolbar into a collapsible settings panel (module, edge types, hops, method toggle).
2. Move zoom buttons into the canvas as a top-right overlay.
3. Add the kind-based visibility filter to the `visibleNodes` memo (orphaned edges drop via `visibleKeys`).
4. `npm run typecheck` + `npm run build`; browser-check the graph screen.
