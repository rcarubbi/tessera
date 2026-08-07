## 1. OpenSpec artifacts

- [ ] 1.1 Create proposal.md
- [ ] 1.2 Create specs/web-dashboard/spec.md delta
- [ ] 1.3 Create design.md
- [ ] 1.4 Create tasks.md
- [ ] 1.5 Validate change with `openspec validate graph-controls`

## 2. GraphView settings area

- [ ] 2.1 Move Module select, edge-type checkboxes, and Expand (hops) into a collapsible "Settings" panel above the canvas (collapsed by default, chevron toggle)
- [ ] 2.2 Move zoom buttons (+ / − / Reset) into the canvas as a top-right overlay

## 3. Method/function visibility toggle

- [ ] 3.1 Add "Show method nodes" toggle (default on) in the settings panel
- [ ] 3.2 Filter `Method` and `Function` nodes in the `visibleNodes` memo when disabled (orphaned edges drop via `visibleKeys`)

## 4. Verification

- [ ] 4.1 `npm run typecheck` + `npm run build`
- [ ] 4.2 Browser check: settings collapse/expand, zoom overlay inside canvas, method toggle hides nodes/edges on a large repo
