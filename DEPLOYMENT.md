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
| `Dashboard__ApiKey` | Bearer key required by `/api/*` (except `/api/github/*`). Admin access for dev; **generate a strong value for production.** |
| `AllowedHosts` | Host header allow-list. Keep `*` only for local/dev. |
| `Cors__AllowedOrigins` | Restrict the dashboard origin if your browser hits CORS errors after locking down `AllowedHosts`. |

### User auth (GitHub OAuth + access scoping)

When `GitHubOAuth__ClientId`/`ClientSecret` are set, users can sign in with
GitHub. Each user's session is scoped to the GitHub App installations they can
access; admins (listed in `Auth__Admins`) see every repository.

| Variable | Description |
|---|---|
| `GitHubOAuth__ClientId` | GitHub OAuth App client ID. Empty disables the "Sign in with GitHub" flow (API key only). |
| `GitHubOAuth__ClientSecret` | GitHub OAuth App client secret. **Never commit it.** |
| `GitHubOAuth__CallbackUrl` | Must match the OAuth App redirect URI, e.g. `http://localhost:5080/api/auth/callback`. |
| `GitHubOAuth__WebUrl` | Web origin for post-login redirect, e.g. `http://localhost:3000`. |
| `Auth__Admins` | Comma-separated GitHub logins granted access to all repositories. |
| `Auth__SessionLifetimeHours` | Session validity in hours (default `12`). |


### GitHub App (ingestion)

| Variable | Description |
|---|---|
| `GitHub__AppId` | GitHub App ID (integer). |
| `GitHub__PrivateKeyPath` | Path to the App's `.pem` inside the `api` container (mount it read-only). |
| `GitHub__WebhookSecret` | Webhook secret; requests without a valid signature are rejected. |
| `GitHub__AppUrl` | Public URL of the dashboard (used in OAuth/App flows). |
| `GitHub__ApiUrl` | Defaults to `https://api.github.com`. |

### LLM provider

The LLM provider is configured entirely through the dashboard
(**Settings > AI**) and stored in the database. Neither the API key nor any
other provider connection detail (base URL, model, embedding model) lives in
environment variables or config files; both the `api` and `worker` processes
pick it up automatically. No provider configured means **structural-only mode**
(rule-based summaries, no LLM calls).

The tuning knobs below stay in config/env:

| Variable | Description |
|---|---|
| `Ai__TopK` | RAG retrieval k (default `5`). |
| `Ai__SimilarityThreshold` | Embedding cosine cutoff (default `0.5`). |
| `Ai__ReviewThreshold` | Nodes with confidence below this go to the review queue (default `0.7`). |
| `Ai__DailyBudgetTokens` | Per-repo daily token budget (default `2,000,000`). |
| `Ai__ComplexityThresholdLines` | Files larger than this are summarized more conservatively. |
| `Ai__RequestsPerMinute` | Max LLM requests per minute (default unlimited). |
| `Ai__MaxRetries` | Retries per LLM call (default `3`). |

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

## Local (offline) repositories

Repositories that live on the host machine can be added from the dashboard
(**Repositories → Add local repository**) instead of through the GitHub App.
They are analyzed only manually (Analyze / Reprocess) — no webhook, no push
trigger. The worker clones the URL verbatim, so mount the repository into the
`worker` container yourself (there is no built-in mount), e.g.

```yaml
# worker service
volumes:
  - /path/to/repo:/repos/local/<name>:ro
```

and use `/repos/local/<name>` as the path when adding it. The name becomes the
clone folder under `Worker__WorkRoot/repos`, so it must be filesystem-safe
(letters, digits, `.`, `-`, `_`). The API container has no git and cannot see
the worker's mounts, so a bad path is only reported when the worker clones
(the repository moves to `Failed` with the error message).

Local repositories are scoped to the user who added them (and admins), recorded
in the `Repositories.CreatedBy` column.

## Hardening notes

- `worker`, `api` and `analyzers` run with a **read-only root filesystem**
  (writes land on named volumes / `/tmp`), `no-new-privileges`, and CPU /
  memory limits. The worker also has a `pids_limit`.
- Repo clones are mounted read-only (`:ro`) into the worker; analysis happens
  in-process inside the container, never against a host path.
- GitHub/OAuth secrets come from environment variables, never from committed
  files; LLM API keys are set through **Settings > AI** and stored in the
  database. Run `./tools/scan-secrets.ps1` (also wired into CI) to catch leaked
  keys.
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
