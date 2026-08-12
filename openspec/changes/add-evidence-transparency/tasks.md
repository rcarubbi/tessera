## 1. OpenSpec artifacts

- [x] 1.1 Create proposal.md
- [x] 1.2 Create specs/evidence-transparency/spec.md
- [x] 1.3 Create specs/web-dashboard/spec.md delta
- [x] 1.4 Create specs/architecture-query/spec.md delta
- [x] 1.5 Create design.md
- [x] 1.6 Create tasks.md
- [x] 1.7 Validate change with `openspec validate add-evidence-transparency`

## 2. Domain

- [x] 2.1 `FactSource` enum: `Ast`, `Git`, `Config`, `Runtime`, `Inference`
- [x] 2.2 `ConfidenceTier` enum: `Verified`, `Accepted`, `LowConfidence`
- [x] 2.3 `EvidenceClassifier`: `ClassifyNode(node)` and `ClassifyEdge(edge)` returning `(classification, factSource, tier)` per documented rules

## 3. Query layer + API

- [x] 3.1 Extend `GraphNodeItem`/`GraphEdgeItem` with classification fields; node detail gains provenance fields
- [x] 3.2 `GraphAsync` accepts `source` (facts/inferences) and `tier` filters
- [x] 3.3 `/nodes`, `/graph` responses include new fields (superset)

## 4. Web

- [x] 4.1 `web/lib/types.ts`: `factSource`, `classification`, `tier` fields
- [x] 4.2 `GraphView.tsx`: source/tier filters (All/Facts/Inferences; All/Verified/Accepted/Low confidence)
- [x] 4.3 Badges on nodes/edges (fact green, inference amber) + entity panel provenance/evidence block
- [x] 4.4 `ReviewPanel.tsx`: copy explaining accept promotes low-confidence inference to verified

## 5. Tests

- [x] 5.1 Derivation matrix: node/edge × fact/inference
- [x] 5.2 Tier boundaries (0.9, 0.7) + review promotion
- [x] 5.3 `source`/`tier` filters deterministic
- [x] 5.4 403 without access

## 6. Verification

- [x] 6.1 `dotnet build Tessera.slnx`
- [x] 6.2 Integration + domain tests green
- [x] 6.3 `npm run typecheck` + `npm run build`
- [x] 6.4 `openspec validate add-evidence-transparency`
- [ ] 6.5 Rebuild images; browser check
