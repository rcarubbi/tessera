## ADDED Requirements

### Requirement: Expandable diagrams
Clicking any rendered Mermaid diagram SHALL open a fullscreen viewer with a zoom-in transition where the diagram is scaled to fit the viewport.

#### Scenario: Click a diagram
- **WHEN** the user clicks a rendered diagram
- **THEN** a fullscreen viewer opens with a zoom-in animation and the diagram fills the viewport

#### Scenario: Close the viewer
- **WHEN** the user clicks the X button (top-right), presses ESC, or clicks the backdrop
- **THEN** the viewer closes and the page returns to its previous state

#### Scenario: Viewer covers all diagram surfaces
- **WHEN** viewing the repository overview diagram, a node class diagram, or a node sequence diagram
- **THEN** the same expandable behavior applies to each diagram

### Requirement: Pan and zoom
The viewer SHALL provide wheel zoom centered on the cursor (clamped 0.25x–8x), pointer-drag panning, and + / − / Reset controls with a zoom indicator.

#### Scenario: Zoom with wheel
- **WHEN** the user scrolls the wheel inside the viewer
- **THEN** the diagram zooms in/out around the cursor position within the clamped range

#### Scenario: Pan when zoomed
- **WHEN** the user drags the diagram while zoomed beyond the viewport bounds
- **THEN** the diagram moves with the pointer so any region can be inspected

#### Scenario: Reset zoom
- **WHEN** the user clicks the − / + buttons or Reset
- **THEN** the zoom level changes accordingly, and Reset restores 100% with the diagram centered
