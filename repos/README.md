# Local (offline) repositories

Drop any git repository folder here to make it available to the worker at
`/repos/local/<folder-name>` (mounted read-only in `docker-compose.yml` via
`${LOCAL_REPOS_DIR:-./repos}:/repos/local:ro`). No per-repo compose edits are
needed — add a folder, then register it from the dashboard:

1. Copy or clone your repo into this directory, e.g. `repos/MyApp/`.
2. Open **Repositories → Add local repository**.
3. Name it (used as the clone folder under the worker work root), enter the
   worker path `/repos/local/MyApp`, and the default branch.
4. **Add** then **Analyze →** to run the first analysis.

Set `LOCAL_REPOS_DIR` in `.env` to host the repos somewhere else.
