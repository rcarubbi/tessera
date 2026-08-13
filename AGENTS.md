# AGENTS.md

Guidelines for AI agents and contributors working in the Tessera codebase. Follow these conventions for .NET, TypeScript, and architecture. When in doubt, match the surrounding code.

## Project Overview

Tessera ingests Git repositories and builds a knowledge graph: nodes (classes, interfaces, methods, functions, modules) with typed edges (Inherits, Implements, Calls, HasMethod, FieldDependency, Injected, InvokesEndpoint). The pipeline (clone → parse → AI-summarize → link → index → overview) runs in a worker; a web dashboard renders graph, overview, per-node diagrams, chat, and reviews. Specs are managed under `openspec/`.

## Repository Layout

- `src/Tessera.Domain` — entities, enums, parsing models, ports (interfaces). **No external dependencies**, no EF/HTTP/AI.
- `src/Tessera.Infrastructure` — adapters: EF Core (`Data/`, `Migrations/`), AI providers (`Ai/`, `Chat/`), analysis (`Analysis/`), queries (`Queries/`), GitHub, Auth, Reviews, Storage.
- `src/Tessera.Api` — minimal API. Endpoint groups in static classes: `AuthEndpoints`, `ChatEndpoints`, `GitHubEndpoints`, `QueryEndpoints`, `ReviewEndpoints`, `SettingsEndpoints`; DI + route mapping in `Program.cs`.
- `src/Tessera.Worker` — background processing: `JobProcessor` (poll loop) and `Pipeline/AnalysisPipeline` (staged pipeline with progress + cancellation).
- `tests/Tessera.Domain.Tests`, `tests/Tessera.Integration.Tests` — xUnit; integration tests use in-memory DB and fake provider registries.
- `analyzers/` — Node.js parse sidecar (`src/analyzer.js`, `src/index.js`); tests with `node:test` in `test/batch.test.js`.
- `web/` — Next.js 15 App Router client app (React 19, TypeScript strict, Tailwind v4, Preline UI).
- `openspec/changes/` — OpenSpec spec-driven changes (validate with `openspec validate <change-id>`).
- `e2e/` — fixture repos used by end-to-end pipeline tests.

## Architecture Principles

- **Clean architecture**: `Domain` defines the model and the **ports** (interfaces). `Infrastructure` implements adapters. `Api`/`Worker` depend only on abstractions, never on concrete adapters.
- **Dependency rule**: references flow inward. `Tessera.Domain` references nothing; `Infrastructure` references `Domain` (+ framework); `Api`/`Worker` reference `Domain` and `Infrastructure`.
- **SOLID**: one responsibility per service; extend by adding providers/implementations (open/closed); keep ports small and behavior-substitutable (interface segregation, Liskov); depend on abstractions (dependency inversion).
- **DRY**: centralize repeated logic (e.g. `RetryPolicy.WithRetryAsync`, regex helpers, `apiGet`/`apiPost` in `web/lib/api.ts`). Duplication is a defect; extract once used twice.
- **KISS**: prefer the simplest correct structure. Do not add a layer/abstraction/generic "just in case". A small focused service beats a clever base class.
- **GoF patterns in practice**: **Adapter** — chat/embedding ports implemented by `OpenAiCompatibleChatProvider` (and future providers); **Factory** — `ProviderRegistry` selects primary/large/fallback providers; **Facade** — `ParserSidecarClient`, `SnapshotComposer` hide subsystem complexity; **Strategy** — `AiSummarizer` vs `RuleBasedSummarizer` vs `RuleBasedArchitect` swap behind `ISemanticSummarizer`; **Repository** — `TesseraDbContext` (EF) and `GraphQueryService` (read models); **Singleton** — long-lived services via DI lifetimes; **Template Method** — staged `AnalysisPipeline` execution.
- **Consequences**: new capabilities usually mean a new interface in `Domain.Ports`, an implementation in `Infrastructure`, an endpoint in `Api`, and DI registration in the relevant `Program.cs` / `Startup`.

## .NET / C# Guidelines

- File-scoped namespaces, 4-space indentation, `sealed` concrete classes, records for DTOs and immutable data (`LinkedEdge`, `OverviewResult`, `ChatMessage`), primary constructors for injected services.
- Naming: `PascalCase` types/members, `_camelCase` for fields, `Async` suffix on async methods, `CancellationToken ct = default` on async signatures.
- DI: constructor injection only; never `new` a service that has dependencies (use DI). Options bound via `IOptions<T>` (e.g. `AiOptions`). Register lifetimes intentionally: request-scoped for services touching `TesseraDbContext`, singleton/transient where stateless.
- Nullable reference types enabled: use `?`/`null!` explicitly; no `// ReSharper` suppressions without reason. Prefer `string.IsNullOrWhiteSpace`, `Array.Empty<T>()`, expression-bodied members.
- Async: `Task`/`ValueTask` all the way down; no `.Result`/`.Wait()`/sync-over-async. Use `ConfigureAwait(false)` only where clearly required.
- EF Core: migrations are generated (`dotnet ef migrations add`), model config in `TesseraDbContext`; query async (`ToListAsync`, `SaveChangesAsync`); add new `DbSet` + migration when adding entities.
- Minimal APIs: group related routes in a static `*Endpoints` class with `app.MapGet/MapPost(...)`. Reuse auth guards (`GuardRepoAsync`, `AccessControlExtensions`); do not duplicate auth logic per endpoint.
- Error handling: surface failures through status codes + readable messages; catch only to translate or roll back; never swallow exceptions silently (unless the established fallback pattern requires it — see AI provider fallback).
- Performance: compiled regexes (`RegexOptions.Compiled`) where hot; avoid LINQ-in-loop; batch EF calls; respect token budgets (`TokenBudgetTracker`).
- Code comments: minimal. Only explain non-obvious intent; never restate the code.

## TypeScript / React Guidelines

- Next.js **App Router**, React function components, hooks only — no class components. Client components start with `"use client"`.
- TypeScript **strict**. Prefer explicit types over `any`; shared API types live in `web/lib/types.ts`; API calls go through `web/lib/api.ts` (`apiGet`/`apiPost`).
- Naming: `PascalCase` components, `camelCase` vars/functions, `UPPER_CASE` constants, kebab-case filenames.
- Browser-only libraries (mermaid, reagraph, Preline) are loaded with `dynamic(() => import(...), { ssr: false })` or dynamic `import()` inside `useEffect` — never imported at module top of a server component.
- State: minimal and local (`useState`/`useMemo`); derive filtered data in memos (see `GraphView` `visibleNodes`/`visibleEdges` pattern). Keep effects clean: guard with a `cancelled` flag on async effects, add/remove event listeners in the same effect.
- Styling: Tailwind v4 utility classes + `@theme` tokens from `globals.css`; Preline UI components for chrome (navbar, cards, badges, tabs, accordion, modal, tooltip, forms). New Preline interactive elements are initialized by `PrelineClient` (`HSStaticMethods.autoInit()` keyed on `usePathname()`); components owning a single Preline root may use manual instances with `destroy()`.
- A11y: semantic elements, `type="button"` on buttons, labels for controls, keyboard-closeable overlays (ESC).
- Error/loading/empty states on every async surface (see `GraphView`, `OverviewPanel`).
- Code comments: minimal; no commented-out code.
- Verify with `npm run typecheck` (`tsc --noEmit`) and `npm run build` in `web/`.

## Testing

- xUnit `[Fact]` for unit/integration; assert with `Assert.*` (`Assert.Single`, `Assert.Contains`, `Assert.Empty`). Test names describe behavior (`Adds_cross_technology_edges_from_llm_response`).
- Integration tests exercise services against an in-memory DB and **fake providers** (see `FakeProviderRegistry`, `FakeChatProvider` in `Tessera.Integration.Tests`). Fakes return canned LLM responses; assert the service's parsing/dedup/filtering behavior.
- `.NET 10` preview target — ignore `NETSDK1057` warnings.
- Analyzers: run with `npm test` in `analyzers/`.
- Windows/PowerShell note: `docker compose exec` with inline SQL breaks under PS quoting — pipe a SQL file via stdin. `StringBuilder.AppendLine` emits `\r\n` on Windows; normalize (`Replace("\r\n", "\n")`) before exact-`\n` assertions.

## Commands

```powershell
# .NET build + tests
dotnet build Tessera.slnx
dotnet test tests/Tessera.Integration.Tests --no-restore
dotnet test tests/Tessera.Domain.Tests --no-restore

# EF migration
dotnet ef migrations add <Name> --project src/Tessera.Infrastructure --startup-project src/Tessera.Api

# Web
cd web
npm run typecheck
npm run build

# Analyzers
cd analyzers
npm test

# OpenSpec
openspec validate <change-id>

# Local stack
docker compose up -d --build --force-recreate api worker analyzers web
```

## Definition of Done

- Follows the repo's architecture (new capability = Domain port + Infrastructure adapter + Api/Worker wiring).
- Complies with SOLID/DRY/KISS and matches surrounding style.
- `.NET` change: builds; related tests green.
- `web` change: `npm run typecheck` + `npm run build` green.
- `analyzers` change: `npm test` green.
- Spec-driven changes documented under `openspec/changes/` and validated.
