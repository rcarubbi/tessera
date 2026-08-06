"use client";

import { useCallback, useEffect, useState } from "react";
import SnapshotSelector from "@/components/SnapshotSelector";
import GraphView from "@/components/GraphView";
import DiffView from "@/components/DiffView";
import ReviewPanel from "@/components/ReviewPanel";
import ChatPanel from "@/components/ChatPanel";
import EntityPanel from "@/components/EntityPanel";
import StatusBadge from "@/components/StatusBadge";
import ReprocessButton from "@/components/ReprocessButton";
import { apiGet } from "@/lib/api";
import type { Repository, Snapshot } from "@/lib/types";

type Tab = "graph" | "diff" | "review" | "chat";

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
      <div className="mx-auto max-w-[1400px] px-5 py-5">
        <div className="card card-error text-danger">{error}</div>
      </div>
    );
  }

  const tabs: { id: Tab; label: string }[] = [
    { id: "graph", label: "Graph" },
    { id: "diff", label: "Diff" },
    { id: "review", label: "Review" },
    { id: "chat", label: "Chat" },
  ];

  return (
    <div className="mx-auto max-w-[1400px] px-5 py-5">
      <div className="flex flex-wrap items-center justify-between gap-4">
        <div className="flex min-w-0 items-center gap-3">
          <div className="min-w-0">
            <h1 className="mb-1 text-xl font-bold">{repo?.fullName ?? "…"}</h1>
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
          <ReprocessButton repoId={repoId} fullName={repo?.fullName ?? repoId} onReprocessed={loadRepo} />
          <SnapshotSelector snapshots={snapshots} commit={commit} onChange={setCommit} loading={snapshotsLoading} />
        </div>
      </div>

      <div className="mb-4 mt-5 flex gap-1 border-b border-border">
        {tabs.map((t) => (
          <button
            key={t.id}
            type="button"
            onClick={() => setTab(t.id)}
            className={`cursor-pointer border-0 border-b-2 bg-transparent px-4 py-2 text-sm ${
              tab === t.id ? "border-accent text-fg" : "border-transparent text-dim hover:text-fg"
            }`}
          >
            {t.label}
          </button>
        ))}
      </div>

      <div className="flex items-stretch gap-4">
        <div className="min-w-0 flex-1">
          {tab === "graph" && (
            <GraphView repoId={repoId} commit={commit} onSelect={openEntity} selectedKey={selectedKey} />
          )}
          {tab === "diff" && <DiffView repoId={repoId} snapshots={snapshots} onSelect={openEntity} />}
          {tab === "review" && <ReviewPanel repoId={repoId} onSelect={openEntity} />}
          {tab === "chat" && <ChatPanel repoId={repoId} commit={commit} onSelect={openEntity} />}
        </div>
        {selectedKey && (
          <div className="w-[420px] shrink-0">
            <EntityPanel repoId={repoId} commit={commit} nodeKey={selectedKey} onClose={() => setSelectedKey(null)} />
          </div>
        )}
      </div>
    </div>
  );
}
