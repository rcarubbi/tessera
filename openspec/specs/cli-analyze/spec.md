# cli-analyze Specification

## Purpose
TBD - created by archiving change add-cli-analyze. Update Purpose after archive.
## Requirements
### Requirement: Analyze a local repository offline
The CLI SHALL provide `tessera analyze <path>` that parses the tracked source files of a local git repository at its HEAD commit using the analyzer sidecar over HTTP, builds nodes and edges in memory, and writes reports to `tessera-report/` in the working directory. The analysis SHALL NOT require a database, an API server, an AI provider, or network access beyond the local sidecar, and SHALL NOT upload source code anywhere.

#### Scenario: Analyze a local repo
- **WHEN** a user runs `tessera analyze /path/to/repo` with a sidecar reachable at the default URL
- **THEN** the CLI reports parse counts and writes `architecture.md`, `dependencies.md`, `impact.md`, and `report.json` under `tessera-report/`.

#### Scenario: No git repository
- **WHEN** the path is not a git repository
- **THEN** the CLI exits with a clear error and writes no reports.

#### Scenario: Sidecar unreachable
- **WHEN** the analyzer sidecar is not reachable
- **THEN** the CLI exits with a clear error naming the URL and how to override it.

### Requirement: Regenerate reports
The CLI SHALL provide `tessera report [dir]` that regenerates the Markdown reports from an existing `report.json` without re-analyzing source code.

#### Scenario: Report regeneration
- **WHEN** a user runs `tessera report` in a directory containing `tessera-report/report.json`
- **THEN** the CLI rewrites the three Markdown reports from the JSON.

#### Scenario: Missing report file
- **WHEN** no `report.json` exists in the target directory
- **THEN** the CLI exits with a clear error.

### Requirement: Markdown report contents
`architecture.md` SHALL contain a module/component inventory with node paths and lines. `dependencies.md` SHALL contain the top dependencies by edge count, detected cycles, and per-entity dependents/dependencies. `impact.md` SHALL contain transitive impact (direct/indirect) for the top-degree nodes with traces.

#### Scenario: Reports generated
- **WHEN** analysis completes successfully
- **THEN** each Markdown report contains its documented sections and only resolvable file:line references.

### Requirement: Validate architecture rules against a report
The CLI SHALL provide `tessera rules validate <rules.yaml> [dir]` that parses the rules YAML and evaluates it against the report's graph, printing violations (rule, severity, paths, lines) and an exit code of 0 when none, non-zero when violations exist.

#### Scenario: Rules pass
- **WHEN** no rule is violated by the report graph
- **THEN** the CLI prints a success summary and exits 0.

#### Scenario: Rules fail
- **WHEN** a deny rule is violated
- **THEN** the CLI prints the violations with file:line and exits non-zero.

#### Scenario: Invalid rules YAML
- **WHEN** the rules file is invalid
- **THEN** the CLI prints the parse error and exits non-zero.

