"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { TopBar } from "@/components/TopBar";
import StatusBadge from "@/components/StatusBadge";
import AddLocalRepo from "@/components/AddLocalRepo";
import DeleteRepoButton from "@/components/DeleteRepoButton";
import { useAuth } from "@/components/AuthContext";
import { apiGet, apiPost, ApiError } from "@/lib/api";
import { badge, card, cardError, field, statCard, statLabel, statValue } from "@/lib/ui";
import type { Repository } from "@/lib/types";

const STATUS_FAILED = 6;

export default function ReposPage() {
  const { user, hydrated, logout } = useAuth();
  const router = useRouter();
  const [repos, setRepos] = useState<Repository[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [query, setQuery] = useState("");
  const [startingId, setStartingId] = useState<string | null>(null);

  const load = () => {
    setError(null);
    apiGet<Repository[]>("/api/repositories")
      .then(setRepos)
      .catch((e) => {
        if (e instanceof ApiError && e.status === 401) {
          logout();
          router.replace("/login");
          return;
        }
        setError(e.message);
      });
  };

  useEffect(() => {
    if (!hydrated) return;
    if (!user) {
      router.replace("/login");
      return;
    }
    load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [user, hydrated, router, logout]);

  const startAnalyze = async (id: string) => {
    setStartingId(id);
    try {
      await apiPost(`/api/repositories/${id}/reprocess`, { mode: "full" });
      load();
    } catch (e) {
      setError(e instanceof ApiError ? e.message : (e as Error).message);
    } finally {
      setStartingId(null);
    }
  };

  const q = query.trim().toLowerCase();
  const filtered =
    repos?.filter(
      (r) =>
        !q ||
        r.fullName.toLowerCase().includes(q) ||
        r.name.toLowerCase().includes(q) ||
        r.owner.toLowerCase().includes(q),
    ) ?? [];

  const stats = {
    total: repos?.length ?? 0,
    analyzing: repos?.filter((r) => r.status === 3 || r.status === 1 || r.status === 2).length ?? 0,
    completed: repos?.filter((r) => r.status === 5).length ?? 0,
    failed: repos?.filter((r) => r.status === STATUS_FAILED).length ?? 0,
  };

  return (
    <div className="app-shell">
      <TopBar />
      <main className="app-main">
        <div className="mx-auto max-w-[1400px] px-5 py-6">
          <div className="mb-6 flex flex-wrap items-center justify-between gap-3">
            <div>
              <h1 className="text-2xl font-bold">Repositories</h1>
              <p className="mt-1 text-sm text-dim">
                Knowledge graphs of connected GitHub and local repositories.
              </p>
            </div>
            <div className="flex flex-wrap items-center gap-3">
              <input
                value={query}
                onChange={(e) => setQuery(e.target.value)}
                placeholder="Search repositories…"
                className={`${field} w-64`}
              />
              <AddLocalRepo onAdded={load} />
            </div>
          </div>

          <div className="mb-6 grid grid-cols-2 gap-3 sm:grid-cols-4">
            <div className={statCard}>
              <span className={statValue}>{stats.total}</span>
              <span className={statLabel}>Total</span>
            </div>
            <div className={statCard}>
              <span className={`${statValue} text-warn`}>{stats.analyzing}</span>
              <span className={statLabel}>Analyzing</span>
            </div>
            <div className={statCard}>
              <span className={`${statValue} text-good`}>{stats.completed}</span>
              <span className={statLabel}>Completed</span>
            </div>
            <div className={statCard}>
              <span className={`${statValue} text-danger`}>{stats.failed}</span>
              <span className={statLabel}>Failed</span>
            </div>
          </div>

          {error && <div className={`${card} ${cardError} mb-4 text-danger`}>{error}</div>}
          {!repos && !error && <div className="text-dim">Loading repositories…</div>}
          {repos && repos.length === 0 && (
            <div className={card}>
              No repositories yet. Connect one via the GitHub App or add a local repository.
            </div>
          )}
          {repos && repos.length > 0 && filtered.length === 0 && (
            <div className={card}>No repositories match &quot;{query}&quot;.</div>
          )}

          <div className="grid grid-cols-[repeat(auto-fill,minmax(340px,1fr))] gap-4">
            {filtered.map((repo) => {
              const failed = repo.status === STATUS_FAILED;
              const local = repo.githubId === 0;
              const inactive = local && !repo.isConnected;
              return (
                <div
                  key={repo.id}
                  className={`flex flex-col gap-2 rounded-xl border bg-panel p-4 transition-colors ${failed ? cardError : "border-border"}`}
                >
                  <div className="flex items-start justify-between gap-3">
                    <Link href={`/repos/${repo.id}`} className="min-w-0 text-fg hover:text-accent">
                      <strong className="block truncate">
                        {repo.fullName}
                        {local && <span className={`${badge} ml-2 align-middle`}>local</span>}
                      </strong>
                    </Link>
                    <StatusBadge status={repo.status} />
                  </div>
                  <div className="text-xs text-dim">
                    {repo.nodeCount} nodes · {repo.edgeCount} edges
                  </div>
                  <div className="text-xs text-dim">
                    {repo.lastProcessedCommit ? (
                      <>
                        last commit <code>{short(repo.lastProcessedCommit)}</code>
                      </>
                    ) : (
                      "no snapshot yet"
                    )}
                  </div>
                  <div className="text-xs text-dim">branch {repo.defaultBranch}</div>
                  <div className="mt-auto flex items-center justify-between gap-2 border-t border-border pt-3">
                    <span className="flex flex-wrap items-center gap-3 text-xs">
                      {inactive && (
                        <button
                          type="button"
                          className="text-accent hover:underline disabled:opacity-50"
                          onClick={() => startAnalyze(repo.id)}
                          disabled={startingId !== null}
                        >
                          {startingId === repo.id ? "Starting…" : "Analyze →"}
                        </button>
                      )}
                      <Link href={`/repos/${repo.id}`} className="text-accent hover:underline">
                        Open graph →
                      </Link>
                      <Link href={`/repos/${repo.id}/progress`} className="text-accent hover:underline">
                        Track progress →
                      </Link>
                    </span>
                    <DeleteRepoButton repoId={repo.id} onDeleted={load} onError={setError} />
                  </div>
                </div>
              );
            })}
          </div>
        </div>
      </main>
    </div>
  );
}

function short(sha: string) {
  return sha.length > 10 ? sha.slice(0, 10) : sha;
}
