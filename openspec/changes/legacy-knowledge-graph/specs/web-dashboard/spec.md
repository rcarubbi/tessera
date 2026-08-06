## ADDED Requirements

### Requirement: Repository dashboard
The web dashboard SHALL list connected repositories with their processing status, last snapshot, node count, and a link to open the graph.

#### Scenario: Repository list
- **WHEN** a user opens the dashboard
- **THEN** the system shows all connected repositories with status and snapshot metadata

### Requirement: Graph view
The web dashboard SHALL render the knowledge graph interactively (entities and edges) with the ability to select a snapshot, focus an entity, expand neighbors, and filter by module or edge type.

#### Scenario: Focus entity neighbors
- **WHEN** a user focuses on an entity in the graph view
- **THEN** the view shows the entity, its direct dependents and dependencies, with edge types

#### Scenario: Snapshot selector
- **WHEN** a user selects a historical commit
- **THEN** the graph view renders that snapshot's graph

### Requirement: Diff view
The web dashboard SHALL render the architectural diff between two selected commits (added/removed/changed entities, new cycles) with navigation to the entity details.

#### Scenario: Compare two commits
- **WHEN** a user selects two commits to compare
- **THEN** the view lists structural changes and highlights new dependency cycles

### Requirement: Entity detail panel
The web dashboard SHALL show the Markdown knowledge node for an entity, including confidence, provenance, dependencies, consumers, and any review status.

#### Scenario: Open entity details
- **WHEN** a user opens an entity
- **THEN** the panel renders its knowledge node, metadata, and linked consumers/dependencies

### Requirement: Review queue
The web dashboard SHALL list entities flagged as "needs review" or "stale" and SHALL allow an authorized user to accept, edit, or dismiss each node. Edits MUST produce a new node version with preserved provenance.

#### Scenario: Review low-confidence node
- **WHEN** a user accepts or edits a node in the review queue
- **THEN** the node leaves the queue and, if edited, is stored as a new version

### Requirement: Chat panel
The web dashboard SHALL embed the chat interface with repository/snapshot context, streaming responses, and clickable citations.

#### Scenario: Chat with citation click
- **WHEN** a user clicks a citation in a chat answer
- **THEN** the app navigates to the cited file/line context

### Requirement: Authentication
The dashboard SHALL require authentication and SHALL scope all data access to repositories the authenticated user can access.

#### Scenario: Unauthenticated access
- **WHEN** an unauthenticated user requests dashboard data
- **THEN** the system rejects the request
