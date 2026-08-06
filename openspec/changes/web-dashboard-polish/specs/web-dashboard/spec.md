## ADDED Requirements

### Requirement: Reprocess repository
The web dashboard SHALL allow an authorized user to re-queue any connected repository for reprocessing. Reprocessing SHALL reset the repository to `Pending` status (clearing `LastProcessedCommit`) so the worker picks it up, and SHALL provide visible loading and success/failure feedback on the card.

#### Scenario: Reprocess from repository card
- **WHEN** an authorized user clicks the reprocess button on a repository card
- **THEN** the dashboard calls `POST /api/repositories/{id}/reprocess` and the card shows an in-progress state until the response returns

#### Scenario: Reprocess API authorization
- **WHEN** a user without access to the repository calls the reprocess endpoint
- **THEN** the API rejects the request with the same authorization rules used by other repository endpoints

#### Scenario: Reprocess failure feedback
- **WHEN** the reprocess request fails
- **THEN** the dashboard shows an error state on the card in red and keeps the repository in its previous state

### Requirement: Explicit error states
The web dashboard SHALL render processing failures distinctly: repositories with `Failed` status and any API error SHALL be displayed with red-colored visual treatment (badge, border, and message) so failures are recognizable at a glance.

#### Scenario: Failed repository card
- **WHEN** a repository has `Failed` status
- **THEN** the card shows a red status badge and a red accent so it is visually distinct from pending/completed cards

#### Scenario: API error on dashboard
- **WHEN** a dashboard request returns an error
- **THEN** the error message is rendered in red with a visible error treatment

### Requirement: Interactive graph controls
The graph view SHALL provide pan and zoom interaction, a node-kind legend, hover highlight of nodes and their connections, and a force-directed layout so large knowledge graphs remain navigable.

#### Scenario: Pan and zoom
- **WHEN** a user drags the canvas or uses the wheel
- **THEN** the graph pans/zooms around the viewport with the current selection preserved

#### Scenario: Node kind legend
- **WHEN** the graph renders
- **THEN** a legend lists the node kinds present with their colors

#### Scenario: Hover highlight
- **WHEN** a user hovers a node
- **THEN** the node and its direct connections are highlighted while other elements dim

### Requirement: Tailwind-based styling
The web dashboard SHALL use Tailwind CSS utility classes for component styling (layout, spacing, typography, colors, states) while preserving the dark GitHub-like theme tokens as the base palette.

#### Scenario: Dashboard renders with Tailwind utilities
- **WHEN** a user loads any dashboard page
- **THEN** layout and components are styled via Tailwind utility classes with consistent dark theme tokens
