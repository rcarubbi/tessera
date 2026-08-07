## ADDED Requirements

### Requirement: Processing time freezes at completion
The progress screen SHALL show the elapsed processing time while a run is in progress and, once the pipeline reaches `Completed`, SHALL stop the counter and display the total processing time.

#### Scenario: Running counter
- **WHEN** the analysis is in progress
- **THEN** the screen shows the elapsed time since the run started, updating every second

#### Scenario: Completed shows total
- **WHEN** the analysis reaches the `Completed` stage
- **THEN** the counter stops and shows the total processing time (`completedAt − analysisStartedAt`) instead of a running value

### Requirement: Reprocess controls on the progress screen
The progress screen SHALL provide "Reprocess all" and "Reprocess missing" actions; the incremental action SHALL expose static and AI analysis options that must have at least one selection to start.

#### Scenario: Reprocess all
- **WHEN** the user clicks "Reprocess all"
- **THEN** the repository is queued for a full reprocess and the screen reflects the pending state

#### Scenario: Reprocess missing with options
- **WHEN** the user clicks "Reprocess missing"
- **THEN** options for "include static analysis" and "include AI analysis" are shown, and the start action is enabled only when at least one is selected

#### Scenario: Controls disabled while running
- **WHEN** the repository is currently processing
- **THEN** the reprocess controls are disabled

### Requirement: Reprocess controls removed from other surfaces
Reprocess actions SHALL only be available on the progress screen.

#### Scenario: No reprocess on list or hub
- **WHEN** viewing the repositories list or a repository hub
- **THEN** no reprocess button is shown
