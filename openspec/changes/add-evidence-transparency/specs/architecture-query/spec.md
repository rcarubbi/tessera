# architecture-query

## MODIFIED Requirements

### Requirement: Graph query
The system exposes graph and node-detail queries scoped to a snapshot (default: latest). Each node and edge SHALL additionally carry its evidence-transparency classification: `factSource`, `classification`, `confidence`, `tier`, and (for edges) `evidence`/`isStatic`; nodes SHALL also carry `commitSha`, `model`, `promptVersion`, and `analyzedAt`. The graph query SHALL accept optional `source` and `tier` filters.

#### Scenario: Classified graph response
- **WHEN** a client fetches the graph
- **THEN** every node and edge includes its classification and tier fields.

#### Scenario: Filtered graph response
- **WHEN** a client requests the graph with a `source` or `tier` filter
- **THEN** the response contains only items matching the filter.
