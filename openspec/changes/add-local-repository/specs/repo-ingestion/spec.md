## ADDED Requirements

### Requirement: Local repository registration
The system SHALL allow any authenticated user to register a local (offline) git repository by name and an absolute path visible inside the worker container.

#### Scenario: Add a local repository
- **WHEN** an authenticated user submits a valid name and an absolute container path
- **THEN** a repository is created with `GitHubId = 0`, `InstallationId = 0`, `Owner = "local"`, `IsConnected = false`, `Status = Pending`, and the creator's login recorded

#### Scenario: Unauthenticated request
- **WHEN** a request to the registration endpoint is made without a valid token
- **THEN** the request is rejected with 401

#### Scenario: Invalid name
- **WHEN** the name is empty, longer than 100 characters, or contains characters other than letters, digits, dots, dashes and underscores
- **THEN** the request is rejected with 400

#### Scenario: Invalid path
- **WHEN** the path is not an absolute container path or contains `..`
- **THEN** the request is rejected with 400

#### Scenario: Duplicate name
- **WHEN** a repository with the same name already exists
- **THEN** the request is rejected with 409

### Requirement: Local repository creator scoping
A local repository SHALL be visible only to the user who registered it and to admins.

#### Scenario: Creator visibility
- **WHEN** the user who registered a local repository lists repositories
- **THEN** the repository is included

#### Scenario: Other users cannot see it
- **WHEN** a different non-admin user lists repositories
- **THEN** the repository is not included

#### Scenario: Admin visibility
- **WHEN** an admin lists repositories
- **THEN** all local repositories are included

### Requirement: Manual activation only
A local repository SHALL run analysis only when the user explicitly requests it; there SHALL be no webhook or push trigger for it.

#### Scenario: Inactive after registration
- **WHEN** a local repository is registered
- **THEN** the worker does not pick it up (it is not connected)

#### Scenario: Analyze activates and queues
- **WHEN** the user requests analysis of an inactive local repository
- **THEN** the repository becomes connected and queued for a full run

#### Scenario: Reprocess reactivates
- **WHEN** the user reprocesses an inactive local repository
- **THEN** the repository becomes connected and queued

## MODIFIED Requirements

### Requirement: Reprocess endpoint
The system SHALL provide a reprocess endpoint that accepts a mode and incremental analysis options, defaults to full reprocess, and persists the request for the worker.

#### Scenario: String mode values
- **WHEN** the reprocess endpoint is called with `mode: "full"` or `mode: "incremental"`
- **THEN** the mode is accepted and applied
