## MODIFIED Requirements

### Requirement: Interactive graph controls (updated)
The graph view SHALL organize its secondary controls (module filter, edge-type checkboxes, hop depth, node visibility) behind a collapsible settings area, and SHALL overlay the zoom controls inside the graph canvas at the top-right corner.

#### Scenario: Collapse and expand settings
- **WHEN** the user toggles the settings control
- **THEN** the module filter, edge-type checkboxes, hop depth select, and node visibility toggle appear/disappear

#### Scenario: Zoom controls inside the canvas
- **WHEN** the graph canvas is visible
- **THEN** the zoom in / zoom out / reset controls are overlaid at the top-right corner of the canvas

### Requirement: Method and function node visibility
The graph view SHALL allow hiding nodes of kind `Method` and `Function` via a toggle (default visible), removing the edges that connect them once hidden.

#### Scenario: Hide method and function nodes
- **WHEN** the user disables "Show method nodes"
- **THEN** `Method` and `Function` nodes are removed from the graph along with edges that only touch them

#### Scenario: Default visibility
- **WHEN** a graph is loaded
- **THEN** `Method` and `Function` nodes are visible by default
