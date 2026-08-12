## Context

`OverviewService` already produces a markdown overview (`## Summary`, `## Main components` with `[key]` markers, `## Architectural notes`, `## Component diagram`) with AI + rule-based fallback. The graph exposes nodes, edges, and per-node metadata. Nothing today turns this into a structured, citation-linked "explain the system" experience. This change is composition + presentation, no new analysis.

## Goals / Non-Goals

**Goals:**
- Structured overview API with clickable, resolvable claims.
- Critical components from deterministic graph centrality.
- Zero new LLM calls (reuses overview generation).

**Non-Goals:**
- No conversational explainer/chat flow (RAG chat already covers free-form questions).
- No per-role tour personalization, no teams/roles modeling.
- No "external systems" inference from code analysis (best-effort only, may be empty).

## Decisions

### D1. ExplainerService composes existing overview + graph
A new `ExplainerService` calls `OverviewService.GenerateAsync` (or reuses a stored overview) and `GraphQueryService` to assemble `ExplainResult`. Overview markdown is parsed into sections; `[key]` markers in `Main components` resolve against the snapshot's node map. Unresolvable keys are dropped (spec: clickable claims only).
- *Why*: reuses tested overview generation and keeps AI policy/fallback unchanged.
- *Alternative considered*: a dedicated AI prompt for explainer (rejected: extra cost/latency, redundant with overview).

### D2. Critical components from degree centrality
`criticalComponents` = top N (default 10) nodes ordered by (in-degree + out-degree) on the snapshot's edges. Pure, deterministic, explainable.
- *Why*: degree is a cheap, honest proxy for "how central is this code"; matches the product's "critical components" need without AI.
- *Alternative considered*: PageRank/betweenness (rejected: overkill now; degree is sufficient and simpler to reason about for MVP).

### D3. Overview markdown parsing is lenient
Sections are matched by header (`## Summary`, `## Main components`, `## Architectural notes`); component bullets are parsed for `[key]`. Missing/malformed sections degrade to empty arrays or raw text — never an error. `externalSystems` is best-effort: extracted from any `## External systems` section if the model emits one, else empty.
- *Why*: model output varies; the structured shape must be robust to format drift.
- *Trade-off*: lenient parsing may drop valid claims — acceptable; the entity panel remains the ground truth.

### D4. Reuse stored overview when available
If the latest snapshot already has a generated overview persisted (via `ProjectOverview`), the explainer uses it and skips regeneration (no token cost); otherwise it generates.
- *Why*: avoids paying tokens twice for the same snapshot.

## Risks / Trade-offs

- **[Model omits `[key]` markers]** → Components list falls back to empty; summary + notes still shown. Mitigated by prompting (existing overview prompt demands `[key]`) and the lenient parser.
- **[Centrality dominated by framework glue]** → Degree centrality can highlight framework-adjacent classes; user can cross-check via entity panel. Acceptable for MVP.
- **[Overview staleness]** → Same snapshot-caching semantics as `OverviewPanel`; regeneration is a reprocess.

## Migration Plan

1. `ExplainerService` + `ExplainResult` records + overview parser + centrality helper (extend `GraphQueryService` with `TopByDegreeAsync`).
2. `GET /api/repositories/{id}/explain` endpoint with access guard.
3. Web: `types.ts`, `ExplainerView` component (steps: Summary / Critical / Explore + mermaid diagram), repo hub entry.
4. Tests: section parsing, claim resolution + unresolvable drop, centrality ordering, rule-based fallback, empty snapshot, 403.
5. Verify: `dotnet build`, integration tests, `npm run typecheck` + `npm run build`, `openspec validate add-system-explainer`.

## Open Questions

- Should the explainer render the full component diagram or only critical components? (Default: full diagram from stored overview; critical list is separate.)
