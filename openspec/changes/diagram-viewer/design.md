## Context

Diagrams are rendered by `web/components/Mermaid.tsx`: it imports `mermaid` client-side, calls `mermaid.render(id, chart)`, and injects the produced SVG into a `div.overflow-auto`. The component is used in two places: (1) `Markdown.tsx` intercepts ```` ```mermaid ```` fences in the AI overview and renders `<Mermaid>`; (2) `EntityPanel.tsx` renders `<Mermaid>` for `node.sequenceDiagram` and `node.classDiagram` in the 420px side panel. The graph canvas (`GraphView.tsx`) already established the pan/zoom interaction users expect (wheel + drag + reset controls).

## Goals / Non-Goals

**Goals:**
- One expandable, fullscreen, zoomable viewer applied to **all** diagram surfaces with a zoom-in transition.
- Viewer interaction consistent with the graph canvas (wheel zoom around cursor, drag to pan, +/−/Reset).
- Zero new runtime dependencies (pan/zoom hand-rolled with CSS transforms).

**Non-Goals:**
- No changes to diagram generation (class/sequence/component diagram content stays as-is).
- No new diagram/rendering library; no portal infrastructure.
- No changes to the graph canvas itself (already has pan/zoom).

## Decisions

### D1. Trigger lives inside the `Mermaid` component
The viewer opens from `Mermaid` itself, so every diagram surface (overview via Markdown, node class/sequence via EntityPanel) gets the same behavior with a single wiring point. The render-error fallback (`<pre>` with raw chart) intentionally has no click handler.

### D2. Hand-rolled pan/zoom (no dependency)
The viewer keeps `{ scale, x, y }` state and applies `transform: translate(x, y) scale(scale)` to the diagram wrapper. Pointer events drive drag-panning; the wheel event zooms around the cursor position (clamped 0.25x–8x). Reset restores `scale=1` centered. **Alternative considered:** `react-zoom-pan-pinch` — rejected to keep the dependency surface minimal for a ~60-line interaction.

### D3. Overlay as a plain `fixed` layer (no portal)
A `position: fixed; inset: 0; z-50` div suffices: no ancestor in the current layout applies `transform/filter/perspective`, and `position: sticky` (EntityPanel) does not create a containing block for fixed descendants. The zoom-in effect is a CSS `@keyframes` (scale 0.9→1, opacity 0→1) on the viewer content.

### D4. Re-render the chart inside the viewer
The overlay renders a fresh `mermaid.render` of the same `chart` in a full-width container, so the SVG uses the overlay's width (mermaid `useMaxWidth` default). SVG is resolution-independent, so the zoomed diagram stays crisp.

## Risks / Trade-offs

- **Double mermaid.render per open** (inline + overlay) — acceptable; render is idempotent and only happens on user click.
- **Fixed overlay inside a sticky panel** — safe today (no transformed ancestors); if a future layout adds `transform`, the overlay must move to a portal.
- **ESC handler** — must be added/removed with the overlay lifecycle to avoid leaking listeners.
- **Very large diagrams** — pan/zoom is transform-only (cheap); mermaid layout cost is unchanged from today.

## Migration Plan

1. Add `DiagramViewer.tsx` (overlay, pan/zoom, controls, close).
2. `Mermaid.tsx`: onClick opens viewer; render-error path excluded.
3. `globals.css`: zoom-in keyframes + overlay control styles.
4. `npm run typecheck` + `npm run build`; rebuild web image; browser-check Overview tab and entity class/sequence diagrams.

## Open Questions

- None blocking.
