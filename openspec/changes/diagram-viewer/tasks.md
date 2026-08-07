## 1. OpenSpec artifacts

- [ ] 1.1 Create proposal.md
- [ ] 1.2 Create specs/diagram-viewer/spec.md delta
- [ ] 1.3 Create design.md
- [ ] 1.4 Create tasks.md
- [ ] 1.5 Validate change with `openspec validate diagram-viewer`

## 2. DiagramViewer component

- [ ] 2.1 Overlay: fixed inset-0 z-50, backdrop, zoom-in keyframe animation, X close top-right
- [ ] 2.2 Close via X / ESC / backdrop click; ESC listener added/removed with lifecycle
- [ ] 2.3 Pan: pointer drag updates translate; wheel zoom centered on cursor, clamped 0.25–8
- [ ] 2.4 Controls: + / − / Reset + zoom percentage (bottom-right)

## 3. Mermaid integration

- [ ] 3.1 `Mermaid.tsx` click opens viewer with the same chart; cursor-pointer on rendered SVG
- [ ] 3.2 Error fallback (`<pre>`) excluded from click behavior
- [ ] 3.3 `globals.css`: zoom-in keyframes + overlay/control styles

## 4. Verification

- [ ] 4.1 `npm run typecheck` + `npm run build`
- [ ] 4.2 Browser check: Overview tab diagram + entity class/sequence diagrams expand, zoom, pan, close
