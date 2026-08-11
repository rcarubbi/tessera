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

## First-time setup

End-to-end walkthrough for a first local run: bring up the stack, connect the
LLM provider through the dashboard, and wire GitHub (ingestion webhook via
Smee + "Sign in with GitHub").

### 1. Prerequisites

- Docker + Docker Compose
- Node.js 18+ (only for the Smee tunnel)
- A GitHub account (to create the GitHub App)
- An LLM API key (e.g. Gemini from https://aistudio.google.com/apikey)

### 2. Bring up the stack

```powershell
docker compose up -d --build
```

- Dashboard: http://localhost:3000
- API: http://localhost:5080
- Analyzers (tree-sitter sidecar): http://localhost:4350

The default dev access key is `dev-dashboard-key` — sign in at
http://localhost:3000/login. Change `Dashboard__ApiKey` before any real use.

### 3. Configure the LLM (Settings > AI)

All provider details (name, base URL, model, API key, embedding model) are set
from the dashboard and stored in the database — **no environment variables or
config files**:

1. Sign in as an admin and open **Settings** (http://localhost:3000/settings).
2. Pick a provider preset (Google Gemini, OpenAI, OpenRouter, Ollama, Custom).
3. Confirm **Base URL** and **Model** (pre-filled by the preset).
4. Paste the **API key**.
5. Optionally set an **Embedding model** (for semantic RAG chat; defaults to
   the chat model).
6. **Save settings**.

Both the API and the worker pick up the change within seconds. Without a
configured provider Tessera runs in **structural-only mode** (AST ground truth,
no AI summaries).

### 4. Create the GitHub App

The same App powers **ingestion** (clone + webhook) and **OAuth login**.

1. GitHub → **Settings → Developer settings → GitHub Apps → New GitHub App**.
2. Name and homepage URL.
3. **Webhook URL**: `https://smee.io/<your-channel>` (channel from step 5).
4. **Webhook secret**: generate one (e.g. `openssl rand -hex 20`) and note it.
5. **Permissions**: *Contents: Read*, *Metadata: Read*.
6. **Subscribe to events**: `push`.
7. **Setup URL**: `http://localhost:5080/api/github/setup` (where GitHub sends
   users after installing the App).
8. Create the App, then copy:
   - the **App ID**,
   - the **Client ID** and **Client secret** (App settings → OAuth), and
   - the **private key**: **Generate a private key**, download the `.pem`, and
     save it to `secrets/tessera-app.pem` (repo root; gitignored).

### 5. Smee tunnel (local webhooks)

GitHub cannot reach `localhost`, so forward webhooks through Smee:

```powershell
npx smee-client --url https://smee.io/<your-channel> --target http://localhost:5080/api/github/webhook
```

Create a channel at https://smee.io (its URL is what you put in the App's
Webhook URL). Keep this terminal running while testing.

### 6. Fill `.env` and restart

```powershell
Copy-Item .env.example .env   # first time only
```

Set at minimum:

| Key | Value |
|---|---|
| `GITHUB_APP_ID` | GitHub App ID (integer) |
| `GITHUB_WEBHOOK_SECRET` | The webhook secret from step 4 |
| `GITHUB_OAUTH_CLIENT_ID` | App Client ID |
| `GITHUB_OAUTH_CLIENT_SECRET` | App Client secret |
| `AUTH_ADMINS` | Comma-separated GitHub logins that see every repo |

`.env` and `secrets/tessera-app.pem` are gitignored — never commit them. Apply
the environment changes:

```powershell
docker compose up -d --force-recreate api worker
```

### 7. Install the App

1. GitHub App page → **Install App** → choose the account/repository.
2. GitHub redirects to the Setup URL; Tessera registers the repository and the
   dashboard opens with `?installed=1`.
3. Push a commit to an installed repo → the webhook (via Smee) triggers the
   worker → clone, parse, AI analysis, snapshot.

Force a re-analysis of already-connected repos:

```powershell
docker compose exec postgres psql -U tessera -d tessera -c "UPDATE \"Repositories\" SET \"Status\" = 0;"
```

### 8. Sign in with GitHub

On the login page use **Sign in with GitHub** — OAuth redirects back to the
dashboard. Logins in `AUTH_ADMINS` see every repository; other users see only
the repos their installations cover.

## Local (offline) repositories

Tessera can also analyze a git repository that lives on the same machine — no
GitHub App, no webhook, no push trigger. Analysis runs **only when you ask**.

The worker runs inside a container, so the repository must be mounted into it
first. There is no built-in mount: add one yourself in `docker-compose.yml`
(worker service), e.g.

```yaml
volumes:
  - ./myrepo:/repos/local/myrepo:ro
```

then:

1. Open **Repositories** and click **Add local repository**.
2. Enter a **Name** (`[A-Za-z0-9._-]`, used as the clone folder), the **path
   inside the worker** (e.g. `/repos/local/myrepo`), and the default branch.
3. **Add** — the card shows a `local` tag and stays inactive.
4. Click **Analyze →** to queue the first full analysis; re-runs use the
   **Reprocess** controls on the repository's progress screen.

Local repositories are visible to the user who added them and to admins. A bad
mount path only surfaces when the worker tries to clone (the repo card shows
the failure). Without a configured LLM provider the run is structural-only,
same as GitHub repositories.

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
