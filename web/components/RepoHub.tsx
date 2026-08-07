"use client";

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import SnapshotSelector from "@/components/SnapshotSelector";
import GraphView from "@/components/GraphView";
import DiffView from "@/components/DiffView";
import ReviewPanel from "@/components/ReviewPanel";
import ChatPanel from "@/components/ChatPanel";
import EntityPanel from "@/components/EntityPanel";
import FilesPanel from "@/components/FilesPanel";
import OverviewPanel from "@/components/OverviewPanel";
import StatusBadge from "@/components/StatusBadge";
import ReprocessButton from "@/components/ReprocessButton";
import { TopBar } from "@/components/TopBar";
import { apiGet } from "@/lib/api";
import type { Repository, Snapshot } from "@/lib/types";

type Tab = "overview" | "files" | "graph" | "diff" | "review" | "chat";

export default function RepoHub({ repoId }: { repoId: string }) {
  const [repo, setRepo] = useState<Repository | null>(null);
  const [snapshots, setSnapshots] = useState<Snapshot[]>([]);
  const [commit, setCommit] = useState<string | null>(null);
  const [tab, setTab] = useState<Tab>("graph");
  const [selectedKey, setSelectedKey] = useState<string | null>(null);
  const [snapshotsLoading, setSnapshotsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const loadRepo = useCallback(() => {
    apiGet<Repository[]>("/api/repositories")
      .then((repos) => setRepo(repos.find((r) => r.id === repoId) ?? null))
      .catch((e) => setError(e.message));
  }, [repoId]);

  useEffect(() => {
    loadRepo();
  }, [loadRepo]);

  useEffect(() => {
    setSnapshotsLoading(true);
    apiGet<Snapshot[]>(`/api/repositories/${repoId}/snapshots`)
      .then(setSnapshots)
      .catch((e) => setError(e.message))
      .finally(() => setSnapshotsLoading(false));
  }, [repoId]);

  const openEntity = useCallback((key: string) => {
    setSelectedKey(key);
  }, []);

  if (error) {
    return (
      <div className="app-shell">
        <TopBar />
        <main className="app-main">
          <div className="mx-auto max-w-[1400px] px-5 py-5">
            <div className="card card-error text-danger">{error}</div>
          </div>
        </main>
      </div>
    );
  }

  const tabs: { id: Tab; label: string }[] = [
    { id: "overview", label: "Overview" },
    { id: "files", label: "Files" },
    { id: "graph", label: "Graph" },
    { id: "diff", label: "Diff" },
    { id: "review", label: "Review" },
    { id: "chat", label: "Chat" },
  ];

  return (
    <div className="app-shell">
      <TopBar />
      <main className="app-main">
        <div className="mx-auto max-w-[1400px] px-5 py-5">
          <div className="mb-4 flex flex-wrap items-center justify-between gap-4">
            <div className="flex min-w-0 items-center gap-3">
              <div className="min-w-0">
                <h1 className="mb-1 text-2xl font-bold">{repo?.fullName ?? "…"}</h1>
                <div className="text-sm text-dim">
                  {repo ? (
                    <>
                      {repo.nodeCount} nodes · {repo.edgeCount} edges · branch {repo.defaultBranch}
                    </>
                  ) : (
                    "…"
                  )}
                </div>
              </div>
              {repo && <StatusBadge status={repo.status} />}
            </div>
            <div className="flex items-center gap-3">
              <Link
                href={`/repos/${repoId}/progress`}
                className="text-sm text-accent hover:underline"
              >
                Track analysis →
              </Link>
              <ReprocessButton repoId={repoId} fullName={repo?.fullName ?? repoId} onReprocessed={loadRepo} />
              <SnapshotSelector snapshots={snapshots} commit={commit} onChange={setCommit} loading={snapshotsLoading} />
            </div>
          </div>

          <div className="mb-4 flex gap-1 rounded-lg border border-border bg-panel p-1">
            {tabs.map((t) => (
              <button
                key={t.id}
                type="button"
                onClick={() => setTab(t.id)}
                className={`flex-1 cursor-pointer rounded-md border-0 px-3 py-2 text-sm font-medium transition-colors ${
                  tab === t.id ? "bg-inset text-fg shadow-sm" : "text-dim hover:text-fg"
                }`}
              >
                {t.label}
              </button>
            ))}
          </div>

          <div className="flex items-stretch gap-4">
            <div className="min-w-0 flex-1">
              {tab === "overview" && <OverviewPanel repoId={repoId} />}
              {tab === "files" && (
                <FilesPanel
                  repoId={repoId}
                  commit={commit}
                  onSelect={(key) => {
                    openEntity(key);
                    setTab("graph");
                  }}
                />
              )}
              {tab === "graph" && (
                <GraphView repoId={repoId} commit={commit} onSelect={openEntity} selectedKey={selectedKey} />
              )}
              {tab === "diff" && <DiffView repoId={repoId} snapshots={snapshots} onSelect={openEntity} />}
              {tab === "review" && <ReviewPanel repoId={repoId} onSelect={openEntity} />}
              {tab === "chat" && <ChatPanel repoId={repoId} commit={commit} onSelect={openEntity} />}
            </div>
            {selectedKey && (
              <div className="w-[420px] shrink-0">
                <EntityPanel
                  repoId={repoId}
                  commit={commit}
                  nodeKey={selectedKey}
                  onClose={() => setSelectedKey(null)}
                  onFocus={(key) => {
                    setSelectedKey(key);
                    setTab("graph");
                  }}
                />
              </div>
            )}
          </div>
        </div>
      </main>
    </div>
  );
}
