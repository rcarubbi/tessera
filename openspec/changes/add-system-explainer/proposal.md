## Why

Onboarding a new developer onto an unfamiliar codebase is the highest-friction moment for any team. Tessera already has the raw material — the semantic overview, the graph, RAG — but presents it as tools, not as an answer to "explain this system to me". A guided, clickable explainer turns the product into onboarding infrastructure.

## What Changes

- **Explain endpoint**: `GET /api/repositories/{id}/explain` returns a structured `ExplainResult` built from the existing overview + graph: `summary`, `main components` (each with node key, symbol, path, line, kind, role), `architectural notes`, `external systems` (best-effort, from overview/edges), and `critical components` (top degree-centrality nodes of the latest snapshot).
- **Clickable claims**: every component and every statement resolves to a node with `file:line` and a link to its entity detail — no unverifiable prose.
- **Guided onboarding view**: a stepped experience in the repo hub — Summary → Critical components → Explore system — where each step renders clickable claims and an optional system component diagram (reusing overview mermaid).
- **No new LLM dependency**: the explainer composes the existing overview (AI or rule-based) with deterministic graph-derived sections. AI is optional and unchanged.

## Capabilities

### New Capabilities
- `system-explainer`: structured, citation-linked system overview (summary, components, critical components, external systems) with clickable `file:line` claims.

### Modified Capabilities
- `web-dashboard`: guided onboarding experience rendering the explainer with clickable claims and component diagram.

## Impact

- `src/Tessera.Infrastructure/Chat/OverviewService.cs` or new `ExplainerService`: parse overview markdown into sections + enrich component keys with node metadata.
- `src/Tessera.Infrastructure/Queries/GraphQueryService.cs`: degree-centrality helper for critical components.
- `src/Tessera.Api/QueryEndpoints.cs`: `/explain` endpoint with access guard.
- `web/lib/types.ts`, `web/components/ExplainerView.tsx` (new), repo hub entry point.
- `tests/Tessera.Integration.Tests/ExplainerTests.cs` (new): section parsing, claim resolution, centrality, fallback to rule-based overview, 403.
