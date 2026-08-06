"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { TopBar } from "@/components/TopBar";
import StatusBadge from "@/components/StatusBadge";
import ReprocessButton from "@/components/ReprocessButton";
import { useAuth } from "@/components/AuthContext";
import { apiGet, ApiError } from "@/lib/api";
import type { Repository } from "@/lib/types";

const STATUS_FAILED = 6;

export default function ReposPage() {
  const { token, logout } = useAuth();
  const router = useRouter();
  const [repos, setRepos] = useState<Repository[] | null>(null);
  const [error, setError] = useState<string | null>(null);

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
    if (!token) {
      router.replace("/login");
      return;
    }
    load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [token, router, logout]);

  return (
    <>
      <TopBar />
      <div className="mx-auto max-w-[1400px] px-5 py-5">
        <div className="mb-5 flex items-center justify-between">
          <h1 className="text-xl font-bold">Repositories</h1>
          {repos && (
            <span className="text-sm text-dim">
              {repos.filter((r) => r.status === STATUS_FAILED).length} failed ·{" "}
              {repos.filter((r) => r.status === 5).length} completed
            </span>
          )}
        </div>

        {error && (
          <div className="card card-error mb-4 text-danger">{error}</div>
        )}
        {!repos && !error && <div className="text-dim">Loading repositories…</div>}
        {repos && repos.length === 0 && (
          <div className="card text-dim">No connected repositories yet.</div>
        )}

        <div className="grid grid-cols-[repeat(auto-fill,minmax(340px,1fr))] gap-4">
          {repos?.map((repo) => {
            const failed = repo.status === STATUS_FAILED;
            return (
              <div
                key={repo.id}
                className={`card flex flex-col gap-2 transition-colors ${failed ? "card-error" : ""}`}
              >
                <div className="flex items-start justify-between gap-3">
                  <Link href={`/repos/${repo.id}`} className="min-w-0 text-fg hover:text-accent">
                    <strong className="block truncate">{repo.fullName}</strong>
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
                  <Link href={`/repos/${repo.id}`} className="text-xs text-accent hover:underline">
                    Open graph →
                  </Link>
                  <ReprocessButton repoId={repo.id} fullName={repo.fullName} onReprocessed={load} />
                </div>
              </div>
            );
          })}
        </div>
      </div>
    </>
  );
}

function short(sha: string) {
  return sha.length > 10 ? sha.slice(0, 10) : sha;
}
