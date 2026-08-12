# web-dashboard

## ADDED Requirements

### Requirement: Guided onboarding view
The web dashboard SHALL provide an onboarding experience ("Explain this system") in the repo hub with steps: Summary, Critical components, Explore. Each step SHALL render clickable claims (component → entity detail with `file:line`) and a component diagram using the existing mermaid renderer.

#### Scenario: Walking through onboarding
- **WHEN** a user opens the explainer for a repository
- **THEN** the dashboard renders the summary step with the overview text, the critical components list with centrality scores, and clickable components linking to entity detail.

#### Scenario: Empty repository
- **WHEN** the repository has no snapshot
- **THEN** the dashboard shows the empty state with a prompt to run analysis.
