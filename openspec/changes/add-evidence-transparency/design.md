## Context

The model already stores the raw ingredients of transparency: `KnowledgeNode.Confidence/Model/PromptVersion/AnalyzedAt/CommitSha`, `GraphEdge.Confidence/IsStatic/Evidence`, `ReviewStatus`. What is missing is a single derived classification (fact vs inference) with a source taxonomy, plus UI that surfaces it. This change is pure derivation + presentation over existing data — no new analysis runs, no new storage.

## Goals / Non-Goals

**Goals:**
- Every node/edge has one unambiguous classification: fact (AST/Git/config) or inference (AI).
- Confidence tiers that respect human review (`ReviewStatus`).
- Filters + badges + provenance detail in the web UI; review panel becomes the promotion path.

**Non-Goals:**
- No change to how analysis stores data (classification is derived at read time).
- No runtime sources (no `Runtime` facts until runtime telemetry exists; enum reserves the slot).
- No configurable per-field confidence (that is rules/override territory — later).

## Decisions

### D1. Classification is derived at query time, not stored
`EvidenceClassifier` derives `(classification, factSource, tier)` from existing fields. No migration, no backfill, no risk of stale classification.
- *Rules*:
  - Node `inference` iff `Model` non-empty OR `Confidence < 1.0`; else `fact` (`factSource = AST`).
  - Edge `fact` iff `IsStatic && Confidence >= 1.0`; else `inference`.
  - Tier: `verified` >= 0.9 or `ReviewStatus = Accepted`; `accepted` >= 0.7; else `low-confidence`.
- *Why*: read-time derivation keeps the model untouched and always correct with current rules.
- *Alternative considered*: persisted classification (rejected: migration + backfill + stale risk for a field that is a pure function of other fields).

### D2. FactSource is a domain enum with Runtime reserved
`FactSource { Ast, Git, Config, Runtime, Inference }`. Today only `Ast` (static edges/nodes) and `Inference` are produced; `Git`/`Config`/`Runtime` are reserved for future provenance (commit history facts, config parsing, runtime traces).
- *Why*: the taxonomy from the product analysis ("source: AST/Git/config/runtime") is the target model; reserving slots avoids a breaking enum change later.

### D3. Filters applied server-side on the graph query
`source` and `tier` params filter nodes/edges in `GraphQueryService.GraphAsync`; the web applies the same labels client-side for styling and a responsive "facts only" toggle.
- *Why*: API filter keeps big graphs cheap to browse; client filter gives instant feedback in the UI. Both use the same derivation logic contract.

### D4. Review panel as promotion path
The existing review accept flow already flips `ReviewStatus = Accepted`, which the tier derivation honors (verified). No new review logic — the UI just explains "reviewing promotes low-confidence inferences to verified".
- *Why*: closes the loop between transparency and the existing review queue with zero new machinery.

## Risks / Trade-offs

- **[Derived rule changes affect historical reads]** → Rules are stable and documented; any future change is a one-line constant update with tests.
- **[`Confidence < 1.0` implies inference for nodes]** → Rule-based summarizers set confidence below 1.0 deliberately; classification treats "rule-based, not AI" as inference (conservative, honest). Documented behavior.
- **[Static but low-confidence edges]** → Impossible today (`IsStatic` implies confidence 1.0 at parse); the classifier still handles the combination defensively.

## Migration Plan

1. Add `FactSource` + `ConfidenceTier` enums and `EvidenceClassifier` in Domain/Infrastructure.
2. Extend `GraphNodeItem`/`GraphEdgeItem` + node detail response; add `source`/`tier` filters to `GraphAsync`.
3. Web: types, badges, filters in `GraphView`, provenance/evidence block in `EntityPanel`, review-panel copy.
4. Tests: derivation matrix (node/edge × fact/inference), tier boundaries, review promotion, filters, 403.
5. Verify: `dotnet build`, integration tests, `npm run typecheck` + `npm run build`, `openspec validate add-evidence-transparency`.

## Open Questions

- Should `config`-derived facts appear in this change? (No — nothing produces them yet; enum reserves the slot.)
