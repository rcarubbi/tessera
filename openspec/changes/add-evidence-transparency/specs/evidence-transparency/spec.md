# evidence-transparency

## ADDED Requirements

### Requirement: Classify nodes and edges as fact or inference
The system SHALL classify every node and edge as `fact` or `inference`. A node SHALL be classified `inference` when its `Model` is non-empty or its `Confidence` is below 1.0; otherwise `fact`. An edge SHALL be classified `fact` when `IsStatic` is true and its confidence is 1.0; otherwise `inference`. Each classification SHALL carry a `factSource`: `AST`, `Git`, or `config` for facts, and `Inference` for AI-derived data.

#### Scenario: Static edge classified as fact
- **WHEN** an edge has `IsStatic = true` and confidence 1.0
- **THEN** the edge is classified `fact` with `factSource = AST`.

#### Scenario: AI node classified as inference
- **WHEN** a node has a non-empty `Model`
- **THEN** the node is classified `inference` with `factSource = Inference`.

### Requirement: Confidence tiers
The system SHALL assign a tier label to every node and edge: `verified` (confidence >= 0.9), `accepted` (0.7 <= confidence < 0.9), or `low-confidence` (confidence < 0.7). Nodes with `ReviewStatus = Accepted` SHALL be labeled `verified` regardless of confidence.

#### Scenario: Tier by confidence
- **WHEN** a node has confidence 0.65
- **THEN** the node is labeled `low-confidence`.

#### Scenario: Reviewed node promoted
- **WHEN** a node with confidence 0.6 has review status `Accepted`
- **THEN** the node is labeled `verified`.

### Requirement: Evidence and provenance in responses
Graph and node API responses SHALL include, for each node: `factSource`, `classification`, `tier`, `confidence`, `commitSha`, `model`, `promptVersion`, and `analyzedAt`. Edge responses SHALL include `factSource`, `classification`, `tier`, `confidence`, `evidence`, and `isStatic`.

#### Scenario: Node detail exposes provenance
- **WHEN** a client fetches node details
- **THEN** the response includes the classification fields and provenance metadata.

#### Scenario: Edge exposes evidence
- **WHEN** a client fetches graph edges
- **THEN** each edge includes its evidence and classification fields.

### Requirement: Filtering by source and tier
The API graph query SHALL accept optional `source` (`facts`, `inferences`) and `tier` filters that restrict returned nodes and edges. Filtering SHALL be deterministic and apply to the latest snapshot unless a commit is given.

#### Scenario: Facts-only graph
- **WHEN** a client requests the graph with `source=facts`
- **THEN** the response contains only fact-classified nodes and edges.

#### Scenario: Low-confidence graph
- **WHEN** a client requests the graph with `tier=low-confidence`
- **THEN** the response contains only low-confidence nodes and edges.
