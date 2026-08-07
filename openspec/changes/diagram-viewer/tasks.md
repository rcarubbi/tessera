## 1. OpenSpec artifacts

- [x] 1.1 Create proposal.md
- [x] 1.2 Create specs/diagram-viewer/spec.md delta
- [x] 1.3 Create design.md
- [x] 1.4 Create tasks.md
- [x] 1.5 Validate change with `openspec validate diagram-viewer`

## 2. DiagramViewer component

- [x] 2.1 Overlay: fixed inset-0 z-50, backdrop, zoom-in keyframe animation, X close top-right
- [x] 2.2 Close via X / ESC / backdrop click; ESC listener added/removed with lifecycle
- [x] 2.3 Pan: pointer drag updates translate; wheel zoom centered on cursor, clamped 0.25–8
- [x] 2.4 Controls: + / − / Reset + zoom percentage (bottom-right)

## 3. Mermaid integration

- [x] 3.1 `Mermaid.tsx` click opens viewer with the same chart; cursor-pointer on rendered SVG
- [x] 3.2 Error fallback (`<pre>`) excluded from click behavior
- [x] 3.3 `globals.css`: zoom-in keyframes + overlay/control styles

## 4. Verification

- [x] 4.1 `npm run typecheck` + `npm run build`
- [x] 4.2 Browser check: Overview tab diagram + entity class/sequence diagrams expand, zoom, pan, close
