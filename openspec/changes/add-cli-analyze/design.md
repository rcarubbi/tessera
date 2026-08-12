## Context

The worker pipeline is orchestrated from `Tessera.Worker` and persists everything to PostgreSQL, but the heavy lifting — parse (sidecar), rule-based summarization, static edge linking, cycle detection, transitive impact — is all in services that run on in-memory data. The CLI extracts that capability into a zero-infrastructure path: no DB, no API, no AI, no upload. It references the same Domain + Infrastructure assemblies, so behavior matches the server pipeline (DRY).

## Goals / Non-Goals

**Goals:**
- `tessera analyze .` works on a machine with only: dotnet runtime, a local analyzer sidecar, and the repo.
- Deterministic, rule-based output — no AI dependency, reproducible reports.
- Self-contained Markdown + JSON artifacts; `tessera report` and `tessera rules ... validate` reuse them.

**Non-Goals:**
- No cloud upload/sync commands in this change.
- No AI summarization in the CLI (rule-based only; AI stays in the SaaS/worker path).
- No web dashboard integration.
- No watch/incremental mode in this change — re-run `analyze` for a fresh snapshot.

## Decisions

### D1. `Tessera.Cli` references Domain + Infrastructure directly
A new console project referencing `Tessera.Domain` and `Tessera.Infrastructure`. DI is composed manually with only the services the CLI needs: `ParserSidecarClient`, `RuleBasedSummarizer` (+ `RuleBasedArchitect`), `ArchitectureLinkingService`, and rules parsing.
- *Why*: reuses battle-tested parsing/linking/summarizer code instead of duplicating it; behavior parity with the worker for static analysis.
- *Trade-off*: the Infrastructure assembly pulls EF/GitHub deps into the CLI's dependency graph. Accepted: it is one published binary; unused services are never instantiated.
- *Alternative considered*: a standalone parser port to Node (rejected: massive duplication, drift risk).

### D2. In-memory graph mirror of `GraphQueryService`
The CLI builds `nodes`/`edges` collections using the same records the API returns (`GraphNodeItem`/`GraphEdgeItem`) and runs impact/cycle/dependency computations as pure functions on those collections. Where feasible it reuses `GraphQueryService`'s static helpers (`FindCycles`, `ImpactAsync` logic) factored out, or a small in-memory port.
- *Why*: identical output semantics to the server impact view, verified against the same fixture repos in tests.
- *Trade-off*: some logic may need extraction into shared helpers so both API and CLI call one implementation (avoid divergence).

### D3. Sidecar is an explicit dependency with a default URL
`tessera analyze` requires the analyzer sidecar at `--analyzer-url` (default `http://localhost:4350`). The CLI walks the repo's tracked files (via `git ls-files`) and sends `ParsedSourceFile` batches to the sidecar, same as the worker.
- *Why*: multi-language parsing already lives in the sidecar; re-implementing tree-sitter in-process is a non-goal. The sidecar is open source and runnable locally, so "no upload" holds — code stays on the machine.
- *Trade-off*: CLI requires Node + sidecar running. Documented; `report`/`rules` subcommands do not need it.

### D4. Reports as a `ReportData` JSON + Markdown renderers
Analysis produces one `report.json` (`ReportData`: commit, counts, nodes, edges, cycles, top deps, impact). Three renderers map it to `architecture.md`, `dependencies.md`, `impact.md`. `tessera report` re-renders from JSON; `tessera rules ... validate` evaluates rules over the JSON graph.
- *Why*: analysis and rendering separate → `report` is pure, and rules validation works without re-analysis (fast CI loop).

### D5. Exit codes as contract
`0` success, `1` validation/rule failure, `2` usage/parse error, `3` sidecar/IO failure. Scriptable in CI.
- *Why*: enables `tessera rules rules.yaml validate && tessera analyze .` in pipelines later.

## Risks / Trade-offs

- **[Logic divergence between CLI and API impact]** → Extract shared pure helpers (impact traversal, cycle detection) into Infrastructure and call from both; CLI tests assert parity with integration tests on the same fixtures.
- **[Infrastructure assembly weight in CLI]** → Publish as a trimmed self-contained binary; measure; revisit with a slimmed interface if too heavy.
- **[Sidecar requirement blocks fully-offline]** → `analyze` needs it; `report` + `rules validate` do not. A future embedded parser could remove the dependency.

## Migration Plan

1. Create `src/Tessera.Cli` project + add to `Tessera.slnx`.
2. Extract shared pure graph helpers (impact traversal, cycle detection, top-deps) into Infrastructure (or keep in CLI referencing Domain-only port if feasible).
3. `Program.cs` dispatch + `AnalyzeCommand` (git ls-files → sidecar parse → rule-based summarize → link → ReportData) + `ReportCommand` + `RulesCommand`.
4. Markdown renderers.
5. Tests (`Tessera.Cli.Tests` or integration): analyze fixture repo → reports contain expected sections; regenerate; rules validate pass/fail/invalid.
6. Docs: AGENTS.md / README section with build + run.
7. Verify: `dotnet build`, `dotnet test`, `openspec validate add-cli-analyze`.

## Open Questions

- Should `analyze` support an `--ai` flag to reuse the worker's AI summarizers? (Deferred: rule-based only in MVP; AI path needs provider config in CLI.)
- Output directory fixed `tessera-report/` vs `--output` flag? (Default fixed; `--output` trivial to add later.)
