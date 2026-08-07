## Why

Mermaid diagrams render small inside scrollable panels: the overview component diagram fits in the `Overview` tab card and per-node class/sequence diagrams render inside the 420px-wide entity panel. Large class hierarchies and sequence flows cannot be inspected in detail, and there is no zoom or pan affordance anywhere except the graph canvas.

## What Changes

- Add a fullscreen **diagram viewer** overlay opened by clicking any rendered Mermaid diagram.
- Opening expands the diagram with a **zoom-in transition**, scaled to fit the viewport.
- Close via an **X button (top-right)**, the **ESC key**, or a **backdrop click**.
- Viewer interaction mirrors the graph canvas: **wheel zoom centered on the cursor** (clamped 0.25x–8x), **pointer-drag panning** when the diagram overflows the screen, and **+ / − / Reset** controls with a live zoom percentage (bottom-right).
- The viewer is wired into the **shared `Mermaid` component**, so every diagram surface gets it automatically: the overview diagram (rendered through `Markdown` mermaid fences) and the node class/sequence diagrams (`EntityPanel`). The render-error fallback (`<pre>` with the raw chart) stays non-clickable.

## Capabilities

### New Capabilities
- `diagram-viewer`: fullscreen, zoomable diagram inspection overlay shared by all Mermaid diagrams.

### Modified Capabilities
- `web-dashboard`: overview diagrams become clickable and expand into the viewer (same behavior as node diagrams).

## Impact

- `web/components/DiagramViewer.tsx` (new): overlay, zoom-in animation, pan/zoom state, close handling.
- `web/components/Mermaid.tsx`: click-to-open, cursor pointer on the rendered SVG.
- `web/app/globals.css`: zoom-in keyframes and overlay/button styles.
- No API, worker, database, or diagram-generation changes.

## Migration Plan

1. Create `DiagramViewer` component with full pan/zoom + close behaviors.
2. Make `Mermaid` open the viewer on click.
3. Add keyframes/styles to `globals.css`.
4. Rebuild the `web` Docker image; verify in the browser (Overview tab + entity class/sequence).
