## ADDED Requirements

### Requirement: Impact analysis
The system SHALL answer "what breaks if I change X?" by computing the transitive closure of dependents of an entity over the graph, returning the affected entities, edge paths, and severity (direct vs indirect).

#### Scenario: Direct dependency change
- **WHEN** a user queries the impact of changing an entity
- **THEN** the system returns all entities that directly depend on it

#### Scenario: Transitive dependency change
- **WHEN** a user queries the impact of changing an entity
- **THEN** the system returns transitive dependents with the dependency path and depth

### Requirement: Consumer lookup
The system SHALL answer "who consumes this event/service/endpoint?" by returning reverse edges — all entities referencing the target, with their node names, paths, and evidence lines.

#### Scenario: Event consumer query
- **WHEN** a user queries consumers of an event entity
- **THEN** the system returns all subscribers/consumers with evidence

### Requirement: Endpoint-to-service mapping
The system SHALL map HTTP endpoints to the controllers, services, and repositories they invoke, using both static edges and AI-inferred relationships with confidence.

#### Scenario: Endpoint chain
- **WHEN** a user queries an endpoint
- **THEN** the system returns the call chain from endpoint to data layer with per-edge confidence

### Requirement: Architectural diff
The system SHALL produce a diff between any two snapshots describing: added, removed, and changed nodes and edges; and MUST detect newly introduced dependency cycles, reporting the cycle path.

#### Scenario: New dependency cycle
- **WHEN** a commit introduces a circular dependency between entities
- **THEN** the diff reports the cycle with its path and the introducing commit

#### Scenario: Node added between commits
- **WHEN** comparing two snapshots where an entity was added
- **THEN** the diff lists the entity as added with its summary

### Requirement: Mermaid export
The system SHALL export graph views in Mermaid format, honoring node filters (subgraph by module, depth limit) for large graphs.

#### Scenario: Export full module graph
- **WHEN** a user exports the graph of a module
- **THEN** the system returns a Mermaid diagram containing that module's entities and edges

#### Scenario: Export depth-limited graph
- **WHEN** a user requests a graph with a depth limit around an entity
- **THEN** the system returns only entities within that depth

### Requirement: Query scoping
All structural queries SHALL target a specific snapshot (commit) by default, falling back to the latest snapshot when none is specified.

#### Scenario: Query without commit
- **WHEN** a user runs a structural query without a commit
- **THEN** the system executes against the latest snapshot

#### Scenario: Query with commit
- **WHEN** a user specifies a commit in a structural query
- **THEN** the system executes against that historical snapshot
