## ADDED Requirements

### Requirement: Reprocess mode selection
A reprocess request SHALL support two modes: full (re-analyze every node from scratch) and incremental (re-analyze only nodes missing the selected analyses).

#### Scenario: Full reprocess
- **WHEN** the user requests a full reprocess
- **THEN** the worker re-parses the repository and re-analyzes every node (AI with rule-based fallback), ignoring incremental options

#### Scenario: Incremental reprocess
- **WHEN** the user requests an incremental reprocess with at least one analysis option (static and/or AI)
- **THEN** the worker processes only nodes missing the selected analyses and reuses existing content for all other nodes

### Requirement: Missing-only incremental analysis
For incremental reprocess, a node SHALL be considered missing an analysis when it has not been produced, is stale, or is of the wrong kind, and SHALL only be re-analyzed for the analyses the user selected.

#### Scenario: Node has static analysis but lacks AI
- **WHEN** the user runs an incremental reprocess with "include AI analysis" and a node already has static (rule-based) content but no AI content
- **THEN** the worker runs AI analysis for that node and the node's content is replaced with the AI result

#### Scenario: Node already has AI analysis
- **WHEN** the user runs an incremental reprocess with "include AI analysis" and a node already has current AI content with unchanged structure
- **THEN** the worker keeps the node's existing content without re-running AI

#### Scenario: Node lacks static content
- **WHEN** the user runs an incremental reprocess with "include static analysis" and a node has no content (empty or `// analysis pending`)
- **THEN** the worker generates static (rule-based) content for that node

#### Scenario: Missing both analyses with AI selected
- **WHEN** a node is missing both static and AI content and the user selected "include AI analysis"
- **THEN** the worker runs AI analysis for the node (which supersedes static content)

#### Scenario: No option selected
- **WHEN** the user requests an incremental reprocess without selecting static or AI analysis
- **THEN** the request is rejected with a validation error

### Requirement: Incremental no-op preserves snapshot
If an incremental reprocess finds no node missing the selected analyses, the worker SHALL keep the existing snapshot unchanged and mark the repository completed.

#### Scenario: Nothing to do
- **WHEN** an incremental reprocess runs and every node already has the selected analyses
- **THEN** the existing snapshot is left untouched and the repository transitions to completed

### Requirement: Processing timestamps
The repository SHALL record when an analysis run starts and when it completes, so the total processing time can be shown.

#### Scenario: Completion timestamp
- **WHEN** an analysis run completes successfully
- **THEN** the repository records the completion time and keeps the run start time from when the run began

#### Scenario: Reprocess resets timestamps
- **WHEN** a reprocess is requested
- **THEN** the previous completion and run-start timestamps are cleared

## MODIFIED Requirements

### Requirement: Reprocess endpoint
The system SHALL provide a reprocess endpoint that accepts a mode and incremental analysis options, defaults to full reprocess, and persists the request for the worker.

#### Scenario: Default full reprocess
- **WHEN** the reprocess endpoint is called without a body
- **THEN** the repository is queued for a full reprocess

#### Scenario: Incremental request with options
- **WHEN** the reprocess endpoint is called with mode `incremental` and `includeAi: true`
- **THEN** the repository is queued for an incremental reprocess restricted to nodes missing AI analysis
