# web-dashboard

## ADDED Requirements

### Requirement: Source and confidence filters
The web dashboard SHALL provide graph filters for source (All / Facts only / Inferences only) and confidence tier (All / Verified / Accepted / Low confidence). Filtering SHALL apply client-side over the loaded graph and update the visible nodes/edges.

#### Scenario: Facts-only view
- **WHEN** a user selects "Facts only"
- **THEN** the graph shows only fact-classified nodes and edges.

#### Scenario: Low-confidence view
- **WHEN** a user selects "Low confidence"
- **THEN** the graph highlights or isolates low-confidence items.

### Requirement: Fact/inference badges and evidence detail
Nodes and edges SHALL display a badge indicating fact (green, source label) or inference (amber, model label). The entity panel SHALL render an evidence/provenance block listing `factSource`, `confidence`, `commitSha`, `model`, `promptVersion`, `analyzedAt`, and edge `evidence` (`file:line`).

#### Scenario: Inspecting an inference
- **WHEN** a user opens the detail of an inference-classified node
- **THEN** the panel shows the badge and the provenance block with model, prompt version, and analyzed timestamp.

#### Scenario: Inspecting a fact edge
- **WHEN** a user inspects a fact-classified edge
- **THEN** the panel shows the fact badge and the `file:line` evidence.
