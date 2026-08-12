## Why

Enterprises that cannot (or will not) send source code to a SaaS still need Tessera's core value. Local repositories are already supported, but there is no offline, zero-upload path. A CLI — `tessera analyze .` → deterministic parse + rule-based analysis → Markdown reports — opens the local-first and on-prem sales motion with no source code leaving the machine.

## What Changes

- **`Tessera.Cli` console project**: a `tessera` command runnable on the developer machine.
- **`tessera analyze <path>`**: reads the local git HEAD commit, walks tracked source files, parses via the analyzer sidecar (local HTTP, default `http://localhost:4350`, overridable), runs rule-based summaries and graph construction **entirely in memory** — no database, no API, no AI, no source upload.
- **Reports**: writes `tessera-report/` with `architecture.md` (modules + component graph), `dependencies.md` (top dependencies, cycles, new cycles), `impact.md` (transitive impact for top-degree nodes), plus `report.json` for the `report` subcommand.
- **`tessera report [dir]`**: regenerates the Markdown reports from a stored `report.json` without re-analyzing.
- **`tessera rules <path> validate`** (optional): validates an architecture-rules YAML file against the report, producing a violations list — reusing the rules engine from `add-architecture-rules`.
- **No infrastructure dependency**: reports are self-contained Markdown; a future `tessera upload` is explicitly out of scope.

## Capabilities

### New Capabilities
- `cli-analyze`: offline local repository analysis via a CLI (parse + rule-based analysis in memory), Markdown/JSON report generation, and rule validation against a generated report.

## Impact

- New project `src/Tessera.Cli/Tessera.Cli.csproj` (references `Tessera.Domain` + `Tessera.Infrastructure`), added to `Tessera.slnx`.
- Reuses `ParserSidecarClient`, `RuleBasedSummarizer`, graph-computation logic from `GraphQueryService` (in-memory variant), and rules parsing from `ArchitectureRuleService`.
- `src/Tessera.Cli/Program.cs`: command dispatch; `AnalyzeCommand`, `ReportCommand`, `RulesCommand`.
- `analyzers/` sidecar: unchanged — CLI calls it over HTTP like the worker.
- `tests/Tessera.Cli.Tests` (new) or integration test project: analyze fixture repo → assert reports; report regeneration; rules validation.
- README/section in AGENTS.md for build/run instructions.
