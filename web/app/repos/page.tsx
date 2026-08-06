"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { TopBar } from "@/components/TopBar";
import StatusBadge from "@/components/StatusBadge";
import { useAuth } from "@/components/AuthContext";
import { apiGet, ApiError } from "@/lib/api";
import type { Repository } from "@/lib/types";

export default function ReposPage() {
  const { token, logout } = useAuth();
  const router = useRouter();
  const [repos, setRepos] = useState<Repository[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!token) {
      router.replace("/login");
      return;
    }
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
  }, [token, router, logout]);

  return (
    <>
      <TopBar />
      <div className="container">
        <h1>Repositories</h1>
        {error && <div className="card" style={{ color: "var(--red)", marginBottom: 16 }}>{error}</div>}
        {!repos && !error && <div className="muted">Loading repositories…</div>}
        {repos && repos.length === 0 && (
          <div className="card muted">No connected repositories yet.</div>
        )}
        <div className="grid">
          {repos?.map((repo) => (
            <Link key={repo.id} href={`/repos/${repo.id}`} style={{ color: "inherit" }}>
              <div className="card">
                <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
                  <strong>{repo.fullName}</strong>
                  <StatusBadge status={repo.status} />
                </div>
                <div className="muted" style={{ marginTop: 8, fontSize: 12 }}>
                  {repo.nodeCount} nodes · {repo.edgeCount} edges
                </div>
                <div className="muted" style={{ fontSize: 12 }}>
                  {repo.lastProcessedCommit ? (
                    <>
                      last commit <code>{short(repo.lastProcessedCommit)}</code>
                    </>
                  ) : (
                    "no snapshot yet"
                  )}
                </div>
                <div className="muted" style={{ fontSize: 12 }}>
                  branch {repo.defaultBranch}
                </div>
              </div>
            </Link>
          ))}
        </div>
      </div>
    </>
  );
}

function short(sha: string) {
  return sha.length > 10 ? sha.slice(0, 10) : sha;
}
