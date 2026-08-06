# Tessera

Reverse engineering de sistemas legados. Conecta um repositório Git e gera um **knowledge graph versionado** da arquitetura — nodes de conhecimento Markdown, amarrados por um **Merkle DAG** para processamento incremental e consultas confiáveis.

## Ideia central

```
GitHub (push) → Worker → clone → tree-sitter (AST estático, custo zero)
                                 ↓
                 só nodes cujo structuralHash mudou → IA semântica
                                 ↓
                 markdown knowledge nodes + semanticHash
                                 ↓
                 Merkle DAG → snapshot por commit (content-addressed)
                                 ↓
                 query layer: "o que quebra se eu mudar X?", diff, chat
```

- **Híbrido**: AST é ground truth (confidence ~1.0). IA só resume e infere semântica — nunca decide estrutura.
- **Dois hashes por node**: `structuralHash` (AST normalizado, muda em mudança estrutural) e `semanticHash` (conteúdo + hashes dos filhos, propaga em cascata).
- **Incremental**: mudou 5 arquivos → IA processa 5 nodes, não o monólito.
- **Time-travel**: snapshot imutável por commit; consultas com versão.

## Estrutura

```
├── src/
│   ├── Tessera.Api/            # ASP.NET Core Web API
│   ├── Tessera.Worker/         # pipeline: clone → parse → analyze → snapshot
│   ├── Tessera.Domain/         # Merkle DAG, knowledge nodes, snapshot composer
│   ├── Tessera.Infrastructure/ # EF Core, object store, git, parser client
│   └── Tessera.Shared/
├── analyzers/                  # sidecar Node.js (web-tree-sitter, 9 gramáticas)
├── tests/
│   └── Tessera.Domain.Tests/   # testes do Merkle DAG
├── web/                        # Next.js (dashboard) — WIP
├── docker-compose.yml
└── openspec/                   # specs da proposta legacy-knowledge-graph
```

## Stack

- Backend: ASP.NET Core (.NET 10), EF Core + PostgreSQL
- Parse: web-tree-sitter (C#, Java, JS/TS, Python, Go, PHP, Ruby)
- Object store: filesystem (dev) → S3-compatible (prod)
- LLM (futuro): DeepSeek/Qwen/GLM via `IChatProvider`

## Desenvolvimento

```powershell
docker compose up -d postgres analyzers
dotnet run --project src/Tessera.Api
dotnet run --project src/Tessera.Worker

# dashboard
docker compose up -d web   # http://localhost:3000 (access key: Dashboard__ApiKey)
cd web; npm run dev        # ou dev mode

# testes
dotnet test
cd analyzers; npm test
cd web; npm run typecheck
./tools/scan-secrets.ps1        # varredura de secrets
./tools/cost-estimate.ps1       # benchmark de custo LLM (requer API de pé)
```

Deploy e configuração (envs, providers LLM, GitHub App, hardening):
[`DEPLOYMENT.md`](DEPLOYMENT.md).

## Status

- [x] Scaffold .NET + sidecar tree-sitter + Docker Compose
- [x] Merkle DAG core (hashes, cascata, snapshot composer) + testes
- [x] Pipeline do Worker (clone → parse → reuso incremental → snapshot)
- [x] Migration EF Core inicial
- [x] E2E: clone → sidecar → snapshot + re-análise incremental (teste de integração)
- [x] Stack Docker de pé (postgres + api + worker + analyzers + web), E2E real com Postgres
- [x] GitHub App: setup callback + webhook push/installation (assinatura HMAC), testes
- [x] Providers LLM: OpenAI-compatible (DeepSeek/Qwen/GLM), retry+backoff, fallback, tiering, budget diário
- [x] Query layer ("o que quebra", diff arquitetural, ciclos, Mermaid) + testes
- [x] Chat RAG (embeddings + fallback lexical, citações file:line, NoContext) + testes
- [x] Web dashboard (Next.js): repos, grafo interativo, diff, review queue, chat streaming, auth por API key + login GitHub OAuth (scoping por usuário/instalação)
- [x] Hardening e QA: CI (dotnet/analyzers/web/scan-secrets), E2E impacto+diff, compose com rootfs read-only + limites, docs de deploy, benchmark de custo LLM
- [ ] GitHub App real (credenciais)

Detalhes técnicos: `openspec/changes/legacy-knowledge-graph/`.
