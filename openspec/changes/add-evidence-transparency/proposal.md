## Why

Enterprise adoption depends on trusting the graph: teams need to tell "Tessera found this in the code" (AST/Git ground truth) from "Tessera believes this" (LLM inference). Nodes and edges already carry `Confidence`, `IsStatic`, `Model`, `PromptVersion`, `Evidence` — but nothing classifies them as fact vs inference, and the web UI hides those fields behind raw JSON. This change makes the distinction first-class and visible.

## What Changes

- **Fact/Inference classification**: every node and edge exposes a derived classification — `fact` (source `AST`, `Git`, or `config`) vs `inference` (AI-generated, with model + prompt version + generated timestamp). A `FactSource` taxonomy drives it: `IsStatic`/null-model edges are facts; non-static edges and AI-content nodes (non-empty `Model`) are inferences.
- **Evidence everywhere**: every edge already carries `evidence` (`file:line`); the API response keeps it and the web UI renders it on the edge. Nodes expose commit/model/promptVersion/analyzedAt.
- **Confidence tiers**: a label per node/edge (verified / accepted / low-confidence) derived from `Confidence` + `ReviewStatus`; low-confidence items get a visible badge and are filterable.
- **Web transparency UI**: the graph/entity views gain a "Source" filter (All / Facts only / Inferences only / Low confidence), badges on nodes and edges, and an evidence/provenance detail block in the entity panel.
- **Review queue alignment**: the existing review panel becomes the place to promote low-confidence inferences to accepted (already exists; this change ties classification to it visually).

## Capabilities

### New Capabilities
- `evidence-transparency`: fact-vs-inference classification of nodes and edges with fact source taxonomy, confidence tiers, and evidence provenance.

### Modified Capabilities
- `web-dashboard`: source/confidence filters, fact/inference badges, evidence + provenance detail in the entity panel.
- `architecture-query`: graph and node responses include classification (`factSource`, `kind`, `tier`) fields.

## Impact

- `src/Tessera.Domain/Enums/FactSource.cs` (new): `Ast`, `Git`, `Config`, `Runtime`, `Inference`; `ConfidenceTier` enum.
- `src/Tessera.Infrastructure/Queries/GraphQueryService.cs`: classification derivation on `GraphNodeItem`/`GraphEdgeItem` + node detail.
- `src/Tessera.Api/QueryEndpoints.cs`: `/nodes`, `/graph` responses gain classification fields (superset).
- `web/lib/types.ts`: classification/tier fields; `web/components/GraphView.tsx`, `EntityPanel.tsx`, `ReviewPanel.tsx`: badges, filters, evidence block.
- `tests/Tessera.Integration.Tests/EvidenceTransparencyTests.cs` (new): classification derivation, tier boundaries, filters.
