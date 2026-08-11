## ADDED Requirements

### Requirement: Add local repository form
The repositories list SHALL provide a way to add a local repository with a name, a path inside the worker, and an optional default branch.

#### Scenario: Add form
- **WHEN** the user opens "Add local repository" and submits a valid name and path
- **THEN** the repository is registered and the list refreshes

#### Scenario: Invalid input feedback
- **WHEN** the submission fails validation
- **THEN** the form shows the server error

### Requirement: Local repository identification
A local repository SHALL be visually identified on the repositories list.

#### Scenario: Local tag
- **WHEN** a repository card has `githubId = 0`
- **THEN** the card shows a "local" tag

### Requirement: Analyze action for inactive local repositories
An inactive local repository SHALL show an explicit "Analyze" action.

#### Scenario: Analyze button
- **WHEN** a local repository is inactive (`isConnected = false`)
- **THEN** the card shows an "Analyze" action that queues a full run
