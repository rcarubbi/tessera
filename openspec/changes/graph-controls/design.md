## Context

`web/components/GraphView.tsx` renders a reagraph canvas inside a bordered, rounded container (`height: calc(100vh - 240px)`). Above the canvas sits a full-width toolbar row holding the module `<select>`, one checkbox per edge type, the Expand (hops) select, and the zoom button group (`+`, `−`, `Reset` — backed by `graphRef.zoomIn/zoomOut/resetControls`). Inside the canvas there is already an absolute Kinds legend overlay (`left-3 top-3`) plus a hover tooltip and a footer stats bar. Node kinds present include `Method` and `Function` (both rendered green), which usually outnumber structural nodes on large repos.

## Goals / Non-Goals

**Goals:**
- Declutter the graph screen: secondary controls behind a collapsible settings area.
- Bring zoom controls next to the canvas (top-right overlay).
- Let users hide `Method`/`Function` nodes to focus on structure.

**Non-Goals:**
- No changes to reagraph usage, layout algorithm, or the graph API payload.
- No change to default visibility semantics beyond the new toggle (methods/functions stay visible by default).
- No persistence of settings across page loads.

## Decisions

### D1. Settings as a collapsible panel, collapsed by default
A slim bar above the canvas holds a "Settings" toggle with a chevron. When expanded it shows: Module select, edge-type checkboxes, Expand (hops) select, and the method-node toggle. Collapsed by default keeps the canvas the visual focus.

### D2. Zoom controls relocated inside the canvas
The `+ / − / Reset` buttons become an `absolute right-3 top-3` overlay inside the canvas container, symmetric with the Kinds legend (`left-3 top-3`). Reuses the existing `GraphCanvasRef` methods unchanged.

### D3. Visibility filter in the `visibleNodes` memo
The method toggle filters inside the existing `visibleNodes` memo (`n.kind !== "Method" && n.kind !== "Function"` when disabled). Because `visibleEdges` already filters by `visibleKeys` (from `visibleNodes`), edges to/from hidden nodes drop automatically — no separate edge-filter logic needed.

### D4. Toggle default: methods/functions visible
Preserves today's behavior; users opt in to hiding. Label reads "Show method nodes" (checked = shown).

## Risks / Trade-offs

- **Expanded settings pushes the canvas down** — acceptable; the canvas already resizes with viewport height.
- **Hidden-node counts in the footer** — the footer still reports full-graph totals (`graph.nodes.length`), so totals remain honest even while rendering is filtered.
- **Kinds legend vs. visibility** — the legend still lists `Method`/`Function` colors when they are hidden; accepted as a legend-of-possible-kinds.

## Migration Plan

1. Collapsible settings panel with module/edge-types/hops + method toggle.
2. Move zoom overlay into the canvas top-right.
3. Extend `visibleNodes` memo with the kind filter (edges follow automatically).
4. Verify: typecheck, build, browser check on a large repo graph.

## Open Questions

- None blocking. (Optional future: persist graph settings per repo in `localStorage`.)
