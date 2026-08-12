# system-explainer

## ADDED Requirements

### Requirement: Structured system overview
The system SHALL produce a structured overview for a repository snapshot (default: latest) containing: `summary`, `mainComponents` (each with node `key`, `symbol`, `path`, `line`, `kind`, and `role`), `architecturalNotes`, `externalSystems` (best-effort), and `criticalComponents`. The overview SHALL reuse the existing semantic overview generation (AI or rule-based fallback) and SHALL NOT require new LLM calls beyond the existing overview.

#### Scenario: Overview generated
- **WHEN** a user requests the explainer for a repository with a snapshot
- **THEN** the system returns the structured overview with all sections populated from the existing overview and graph.

#### Scenario: Rule-based fallback
- **WHEN** no AI provider is available
- **THEN** the explainer returns the rule-based overview structure with the same section shape.

#### Scenario: No snapshot
- **WHEN** the repository has no snapshot yet
- **THEN** the explainer returns an empty-state response indicating analysis has not run.

### Requirement: Clickable claims resolve to source
Every component in `mainComponents` and every item in `criticalComponents` SHALL carry a node `key` and `path:line`, enabling the client to link to the entity detail. Claims without a resolvable node SHALL be omitted from the components list (never rendered as unclickable prose).

#### Scenario: Component claim resolves
- **WHEN** a component references a node that exists in the snapshot
- **THEN** the component includes the node key, path, and start line.

#### Scenario: Unresolvable claim dropped
- **WHEN** the overview names a component with no matching node key in the snapshot
- **THEN** the component is omitted from `mainComponents`.

### Requirement: Critical components by centrality
The system SHALL compute `criticalComponents` as the top N (default 10) nodes of the snapshot by degree centrality (in-degree + out-degree over graph edges), each with the centrality score.

#### Scenario: Critical list returned
- **WHEN** a user requests the explainer
- **THEN** the top-degree nodes are returned with their centrality scores, ordered descending.

### Requirement: Explain endpoint via API
`GET /api/repositories/{repositoryId}/explain` SHALL return the structured overview and SHALL require repository access.

#### Scenario: Authorized request
- **WHEN** an authorized user calls the explain endpoint
- **THEN** the API returns the structured overview.

#### Scenario: Unauthorized request
- **WHEN** a user without repository access calls the endpoint
- **THEN** the API returns 403.
