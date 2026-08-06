## ADDED Requirements

### Requirement: GitHub App connection
The system SHALL allow a user to connect their GitHub account via OAuth and authorize installation of the Tessera GitHub App on repositories. Each connection MUST persist the installation id and the list of accessible repositories.

#### Scenario: User connects GitHub account
- **WHEN** user completes the GitHub OAuth flow
- **THEN** the system stores the installation and lists all repositories the App can access

#### Scenario: Installation removed on GitHub
- **WHEN** the GitHub App is uninstalled from a repository
- **THEN** the system marks the repository as disconnected and stops processing it

### Requirement: Push event ingestion
The system SHALL receive GitHub `push` webhooks for installed repositories and enqueue an analysis job for the pushed commit.

#### Scenario: New push to repository
- **WHEN** GitHub delivers a `push` event for an installed repository
- **THEN** the system enqueues an incremental analysis job for that commit

#### Scenario: Unknown repository event
- **WHEN** a webhook arrives for a repository with no active installation
- **THEN** the system ignores the event without error

### Requirement: Incremental clone
The system SHALL clone a repository incrementally: initial full clone on first connection, and `git fetch` + diff against the last processed commit on subsequent events. The analysis worker MUST run inside an isolated container with a time-bounded checkout.

#### Scenario: First connection to a repository
- **WHEN** a repository is connected for the first time
- **THEN** the system performs a full clone of the default branch

#### Scenario: Subsequent push after a processed commit
- **WHEN** a push arrives and a previous commit is already processed
- **THEN** the system fetches new objects and computes the diff against the last processed commit

### Requirement: Repository state tracking
The system SHALL persist per-repository processing state: last processed commit sha, branch, and job status. Reprocessing MUST be idempotent — the same commit SHA SHALL produce the same snapshot.

#### Scenario: Duplicate webhook for same commit
- **WHEN** the same commit is delivered twice
- **THEN** the system processes it once and returns the existing snapshot

### Requirement: Analysis isolation
The system SHALL execute repository cloning and analysis in disposable containers with memory, CPU, and time limits, and MUST never execute code found in the repository.

#### Scenario: Malicious repository files
- **WHEN** a repository contains scripts that attempt execution during analysis
- **THEN** the analysis runs read-only and no repository code is executed
