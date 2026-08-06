## ADDED Requirements

### Requirement: Content-addressed storage
The system SHALL store each knowledge node as an immutable object named by its `semanticHash` in an object store (filesystem in dev, S3-compatible in prod). Writing the same hash twice MUST be a no-op.

#### Scenario: Store a knowledge node
- **WHEN** a node's Markdown and metadata are finalized
- **THEN** the system persists the object under its `semanticHash` key

#### Scenario: Duplicate hash write
- **WHEN** a node with an existing hash is written again
- **THEN** the system does not duplicate the object

### Requirement: Snapshot per commit
The system SHALL create a snapshot object for each processed commit containing: commit sha, root hash (SHA-256 over the sorted set of all node `semanticHash` values), node count, and edge count.

#### Scenario: Snapshot creation
- **WHEN** an analysis job for a commit completes
- **THEN** the system persists a snapshot with a root hash that changes if any node in the repository changed

#### Scenario: Unchanged commit rebuild
- **WHEN** a commit produces the same set of node hashes as a prior snapshot
- **THEN** the system stores the snapshot with the same root hash

### Requirement: Cascade invalidation
The system SHALL recompute `semanticHash` bottom-up through dependents when a child node changes. Recomputing a hash SHALL NOT trigger LLM calls unless the node's own content is stale by configured policy.

#### Scenario: Child change propagates
- **WHEN** a changed entity produces a new node hash
- **THEN** all direct dependents recompute their hash, and the new root hash reflects the change

### Requirement: Snapshot time-travel
The system SHALL retain historical snapshots and SHALL support querying the graph as of any processed commit.

#### Scenario: Query historical snapshot
- **WHEN** a user requests the graph state for a past commit
- **THEN** the system returns nodes and edges exactly as stored in that snapshot

### Requirement: Storage consistency
The object store and the PostgreSQL index MUST be consistent: every indexed node hash MUST have a matching object, and the snapshot root hash MUST equal a recomputed hash over indexed nodes.

#### Scenario: Consistency check
- **WHEN** a verification job runs
- **THEN** the system detects and reports any object missing from the store or index
