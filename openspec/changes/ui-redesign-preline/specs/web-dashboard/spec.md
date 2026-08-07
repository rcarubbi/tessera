## MODIFIED Requirements

### Requirement: Preline-based component styling (updated)
The web dashboard SHALL render all surfaces using Preline components consistent with the dark theme tokens: navbar (TopBar), cards/badges/buttons (repo list, status), tabs (repo hub), accordion (graph settings), button group (zoom), legend indicator and tooltip (graph overlays), modal/overlay (diagram viewer), chat bubbles/toasts (chat), and input/select/checkbox/switch (forms and graph settings).

#### Scenario: All dashboard surfaces use Preline markup
- **WHEN** any dashboard page is loaded
- **THEN** its components are rendered with Preline/utility classes instead of the legacy bespoke classes

#### Scenario: Interactive states run through Preline plugins
- **WHEN** the user interacts with tabs, the settings accordion, the diagram viewer modal, dropdowns, tooltips, or toggles
- **THEN** the behavior is driven by Preline JS plugins after hydration

### Requirement: Existing functionality preserved
Restyling SHALL NOT change existing behavior: graph pan/zoom and node selection, the diagram viewer (zoom-in open, X/ESC/backdrop close, pan/zoom), chat, reprocess feedback, and the graph settings (module/edge types/hops/method toggle) continue to work.

#### Scenario: Graph interactions unaffected
- **WHEN** the user pans, zooms, or clicks a node on the graph canvas
- **THEN** the reagraph canvas behaves exactly as before

#### Scenario: Diagram viewer still works
- **WHEN** the user clicks a diagram
- **THEN** the viewer opens with the zoom-in transition, supports pan/zoom, and closes via X/ESC/backdrop

#### Scenario: Graph settings still work
- **WHEN** the user expands the settings accordion and toggles module, edge types, hops, or method/function visibility
- **THEN** the graph filters exactly as before
