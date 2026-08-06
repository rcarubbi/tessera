# Deploying Tessera

Tessera runs as Docker Compose stack. This document covers the runtime
configuration, LLM providers, GitHub App setup, and operational notes.

## Quick start

```powershell
docker compose up --build -d
```

| Service | Address |
|---|---|
| Dashboard (web) | http://localhost:3000 |
| API | http://localhost:5080 |
| Analyzer sidecar | http://localhost:4350 (internal) |
| PostgreSQL | localhost:5432 |

The dashboard logs in with the API key (default `dev-dashboard-key`).
Change it before any non-local use (see below).

## Environment variables

Set values either in `docker-compose.yml` or via an `.env` file (Compose
expands `${VAR}` references).

### Database

| Variable | Default | Description |
|---|---|---|
| `ConnectionStrings__TesseraDb` | `Host=postgres;...` | EF Core / Npgsql connection string |

### Dashboard / API security

| Variable | Description |
|---|---|
| `Dashboard__ApiKey` | Bearer key required by `/api/*` (except `/api/github/*`). **Generate a strong value for production.** |
| `AllowedHosts` | Host header allow-list. Keep `*` only for local/dev. |
| `Cors__AllowedOrigins` | Restrict the dashboard origin if your browser hits CORS errors after locking down `AllowedHosts`. |

### GitHub App (ingestion)

| Variable | Description |
|---|---|
| `GitHub__AppId` | GitHub App ID (integer). |
| `GitHub__PrivateKeyPath` | Path to the App's `.pem` inside the `api` container (mount it read-only). |
| `GitHub__WebhookSecret` | Webhook secret; requests without a valid signature are rejected. |
| `GitHub__AppUrl` | Public URL of the dashboard (used in OAuth/App flows). |
| `GitHub__ApiUrl` | Defaults to `https://api.github.com`. |

### LLM providers

Providers are configured as a list; reference one as `Primary` or `Fallback`
by `Name`:

| Variable | Description |
|---|---|
| `Ai__Providers__0__Name` | Logical name, e.g. `deepseek` or `qwen`. |
| `Ai__Providers__0__BaseUrl` | OpenAI-compatible base URL (e.g. `https://api.deepseek.com/v1`). |
| `Ai__Providers__0__ApiKey` | API key. **Never commit it.** |
| `Ai__Providers__0__Model` | Chat model id (e.g. `deepseek-chat`). |
| `Ai__Providers__0__Endpoint` | Chat path, default `chat/completions`. |
| `Ai__Providers__0__EmbeddingModel` | Embedding model id (optional). |
| `Ai__Providers__0__EmbeddingEndpoint` | Embedding path, default `embeddings`. |
| `Ai__Primary` / `Ai__Fallback` | Which provider names to use; falls back on errors. |
| `Ai__Embedding` | Provider name used for embeddings (RAG). |
| `Ai__TopK` | RAG retrieval k (default `5`). |
| `Ai__SimilarityThreshold` | Embedding cosine cutoff (default `0.5`). |
| `Ai__ReviewThreshold` | Nodes with confidence below this go to the review queue (default `0.7`). |
| `Ai__DailyBudgetTokens` | Per-repo daily token budget (default `2,000,000`). |
| `Ai__ComplexityThresholdLines` | Files larger than this are routed to the `LargeTier` provider. |

Leave `Ai__Primary`/`Ai__Fallback` empty to run in **structural-only mode**
(rule-based summaries, no LLM calls).

## GitHub App setup

1. Create a GitHub App (Settings > Developer settings > GitHub Apps).
2. Permissions: **Contents: Read**, **Metadata: Read**; subscribe to the
   **push** webhook event.
3. Webhook URL: `https://<host>/api/github/webhook`, with a secret.
4. Generate a private key, download the `.pem`, and mount it into the `api`
   container at `GitHub__PrivateKeyPath`.
5. Set `GitHub__AppId`, `GitHub__WebhookSecret`, and `GitHub__AppUrl`.
6. Install the App on the target organizations/repos.

Push events register the repository; the worker then clones and analyzes it.
To (re)process a repository manually:

```sql
UPDATE "Repositories" SET "Status" = 0;
```

(The worker skips repos whose `head` equals `LastProcessedCommit`.)

## Hardening notes

- `worker`, `api` and `analyzers` run with a **read-only root filesystem**
  (writes land on named volumes / `/tmp`), `no-new-privileges`, and CPU /
  memory limits. The worker also has a `pids_limit`.
- Repo clones are mounted read-only (`:ro`) into the worker; analysis happens
  in-process inside the container, never against a host path.
- Secrets come from environment variables, never from committed files. Run
  `./tools/scan-secrets.ps1` (also wired into CI) to catch leaked keys.
- Git repos are cloned with `--depth` into `Worker__WorkRoot`; keep that
  volume private (it contains customer source).
- Migrations: the API applies `EnsureCreated`/migrations on startup. Use a
  dedicated migration in a real deployment instead of racing several workers.

## LLM cost calibration

`tools/cost-estimate.ps1` benchmarks cost against a running API:

```powershell
$env:TESSERA_API = "http://localhost:5080"
$env:TESSERA_API_KEY = "dev-dashboard-key"
./tools/cost-estimate.ps1 -PricePerMillionInput 0.28
```

It compares the last two snapshots and reports the token surface of a full
pass versus an incremental pass (nodes whose content changed). Use it to:

- validate that `structuralHash` is stable under cosmetic edits (low
  incremental cost on small diffs);
- calibrate `Ai__ReviewThreshold` — raise it if too many low-confidence nodes
  pile up in review, lower it if reviewed nodes are consistently accurate;
- size `Ai__DailyBudgetTokens` per repository.
