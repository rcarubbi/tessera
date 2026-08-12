## 1. OpenSpec artifacts

- [x] 1.1 Create proposal.md
- [x] 1.2 Create specs/cli-analyze/spec.md
- [x] 1.3 Create design.md
- [x] 1.4 Create tasks.md
- [x] 1.5 Validate change with `openspec validate add-cli-analyze`

## 2. Project setup

- [ ] 2.1 Create `src/Tessera.Cli/Tessera.Cli.csproj` (console, refs Domain + Infrastructure); add to `Tessera.slnx`
- [ ] 2.2 Manual DI composition for CLI-only services (ParserSidecarClient, RuleBasedSummarizer, RuleBasedArchitect, ArchitectureLinkingService, rule parsing)

## 3. Shared graph helpers

- [ ] 3.1 Extract shared pure helpers (transitive impact traversal, cycle detection, top-dependencies) so API and CLI call one implementation

## 4. Commands

- [ ] 4.1 `Program.cs` dispatch with exit-code contract (0 success, 1 rule failure, 2 usage/parse, 3 sidecar/IO)
- [ ] 4.2 `AnalyzeCommand`: git HEAD commit → `git ls-files` → sidecar parse batches → rule-based summarize → link → `ReportData`
- [ ] 4.3 `ReportData` + JSON serialize to `tessera-report/report.json`
- [ ] 4.4 `ReportCommand`: regenerate Markdown from existing `report.json`
- [ ] 4.5 `RulesCommand`: parse rules YAML + evaluate over report graph, print violations, exit 0/1/2
- [ ] 4.6 Markdown renderers: `architecture.md`, `dependencies.md`, `impact.md` with resolvable file:line references

## 5. Tests

- [ ] 5.1 Analyze fixture repo (`e2e/`): reports written with expected sections + counts
- [ ] 5.2 Report regeneration from JSON
- [ ] 5.3 Rules validate: pass / fail (deny violation) / invalid YAML exit codes
- [ ] 5.4 Error paths: not-a-git-repo, sidecar unreachable, missing report.json

## 6. Docs + verification

- [ ] 6.1 AGENTS.md / README section: build, sidecar requirement, usage examples
- [ ] 6.2 `dotnet build Tessera.slnx`
- [ ] 6.3 `dotnet test` (domain + integration + CLI) green
- [ ] 6.4 `openspec validate add-cli-analyze`
