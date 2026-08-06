## ADDED Requirements

### Requirement: Knowledge node generation
The system SHALL generate one Markdown knowledge node per entity, containing: title, type, responsibilities, dependencies, incoming/outgoing calls, events published/consumed, and a confidence score. Nodes MUST be produced only for entities whose `structuralHash` changed or that have no prior node.

#### Scenario: New entity without prior node
- **WHEN** an entity appears for the first time in a snapshot
- **THEN** the system generates a Markdown knowledge node for it

#### Scenario: Entity with unchanged structure
- **WHEN** an entity's `structuralHash` equals the last processed hash
- **THEN** the system reuses the existing node and does not call the LLM

### Requirement: Provider abstraction
The system SHALL expose a provider abstraction (`IChatProvider`) supporting DeepSeek, Qwen, and GLM via OpenAI-compatible endpoints, configurable per deployment. Provider failures MUST trigger fallback to a configured secondary provider.

#### Scenario: Primary provider failure
- **WHEN** the primary LLM provider returns an error or times out
- **THEN** the system retries with backoff and then falls back to the secondary provider

#### Scenario: Custom provider configuration
- **WHEN** an operator configures a provider with base URL, model, and API key
- **THEN** the system uses that provider for semantic analysis without code changes

### Requirement: Semantic hash
The system SHALL compute a `semanticHash` per node from its Markdown content plus the `semanticHash` of its child edges (sorted). Any content or child change MUST change the parent's hash.

#### Scenario: Parent hash reflects child change
- **WHEN** a child node's content changes
- **THEN** the parent node's `semanticHash` changes without regenerating the parent's Markdown

### Requirement: Confidence and provenance
Each node MUST record a confidence score (0-1) and provenance: commit sha, model identifier, prompt version, and timestamp. The system SHALL flag nodes below a configurable confidence threshold as needing human review.

#### Scenario: Low-confidence node
- **WHEN** a node's confidence is below the configured threshold
- **THEN** the system adds the node to the human review queue and marks it "needs review"

#### Scenario: Prompt change invalidation
- **WHEN** the prompt version used to generate a node differs from the current prompt version
- **THEN** the system records the node as stale and eligible for optional regeneration

### Requirement: Token budget and tiering
The system SHALL enforce per-repository and per-day LLM token budgets, and SHALL route analysis to a cheaper/smaller model for simple entities and a larger model for complex ones.

#### Scenario: Budget exhausted
- **WHEN** a repository's daily token budget is exhausted
- **THEN** the system pauses LLM analysis and resumes when the budget resets

#### Scenario: Complex entity routing
- **WHEN** an entity exceeds a size/complexity threshold
- **THEN** the system routes it to the larger model tier
