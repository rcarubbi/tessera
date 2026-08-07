# Tessera

Reverse engineering for legacy systems. Connect a Git repository and generate a **versioned knowledge graph** of its architecture — Markdown knowledge nodes, linked by a **Merkle DAG** for incremental processing and reliable queries.

## Core idea

```
GitHub (push) → Worker → clone → tree-sitter (static AST, zero cost)
                                 ↓
                 only nodes whose structuralHash changed → semantic AI
                                 ↓
                 markdown knowledge nodes + semanticHash
                                 ↓
                 Merkle DAG → snapshot per commit (content-addressed)
                                 ↓
                 query layer: "what breaks if I change X?", diff, chat
```

- **Hybrid**: the AST is the ground truth (confidence ~1.0). AI only summarizes and infers semantics — it never decides structure.
- **Two hashes per node**: `structuralHash` (normalized AST, changes on structural changes) and `semanticHash` (content + child hashes, propagates in cascade).
- **Incremental**: 5 files changed → AI processes 5 nodes, not the monolith.
- **Time-travel**: immutable snapshot per commit; queries carry a version.

## Structure

```
├── src/
│   ├── Tessera.Api/            # ASP.NET Core Web API
│   ├── Tessera.Worker/         # pipeline: clone → parse → analyze → snapshot
│   ├── Tessera.Domain/         # Merkle DAG, knowledge nodes, snapshot composer
│   ├── Tessera.Infrastructure/ # EF Core, object store, git, parser client
│   └── Tessera.Shared/
├── analyzers/                  # Node.js sidecar (web-tree-sitter, 9 grammars)
├── tests/
│   ├── Tessera.Domain.Tests/   # Merkle DAG tests
│   └── Tessera.Integration.Tests/
├── web/                        # Next.js dashboard
├── docker-compose.yml
└── openspec/                   # spec-driven changes (legacy-knowledge-graph proposal)
```

## Stack

- Backend: ASP.NET Core (.NET 10), EF Core + PostgreSQL
- Parse: web-tree-sitter (C#, Java, JS/TS, Python, Go, PHP, Ruby)
- Object store: filesystem (dev) → S3-compatible (prod)
- LLM: OpenAI-compatible providers (Gemini, DeepSeek/Qwen/GLM) via `IChatProvider`

## Development

```powershell
docker compose up -d postgres analyzers
dotnet run --project src/Tessera.Api
dotnet run --project src/Tessera.Worker

# dashboard
docker compose up -d web   # http://localhost:3000 (access key: Dashboard__ApiKey)
cd web; npm run dev        # or dev mode

# tests
dotnet test
cd analyzers; npm test
cd web; npm run typecheck
./tools/scan-secrets.ps1        # secrets scan
./tools/cost-estimate.ps1       # LLM cost benchmark (requires a running API)
```

Deployment and configuration (envs, LLM providers, GitHub App, hardening):
[`DEPLOYMENT.md`](DEPLOYMENT.md).

## Status

- [x] Scaffold .NET + tree-sitter sidecar + Docker Compose
- [x] Merkle DAG core (hashes, cascade, snapshot composer) + tests
- [x] Worker pipeline (clone → parse → incremental reuse → snapshot)
- [x] Initial EF Core migration
- [x] E2E: clone → sidecar → snapshot + incremental re-analysis (integration test)
- [x] Full Docker stack (postgres + api + worker + analyzers + web), real E2E with Postgres
- [x] GitHub App: setup callback + push/installation webhook (HMAC signing), tests
- [x] LLM providers: OpenAI-compatible (Gemini, DeepSeek/Qwen/GLM), retry+backoff, fallback, tiering, daily budget
- [x] Query layer ("what breaks", architectural diff, cycles, Mermaid) + tests
- [x] RAG chat (embeddings + lexical fallback, file:line citations, NoContext) + tests
- [x] Web dashboard (Next.js): repos, interactive graph, diff, review queue, streaming chat, API-key auth + GitHub OAuth login (per-user/installation scoping)
- [x] Hardening and QA: CI (dotnet/analyzers/web/scan-secrets), impact+diff E2E, read-only rootfs + limits in compose, deploy docs, LLM cost benchmark
- [ ] Real GitHub App (credentials)

Technical details: `openspec/changes/legacy-knowledge-graph/`.
