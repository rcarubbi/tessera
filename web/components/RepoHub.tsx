"use client";

import { useCallback, useEffect, useState } from "react";
import SnapshotSelector from "@/components/SnapshotSelector";
import GraphView from "@/components/GraphView";
import DiffView from "@/components/DiffView";
import ReviewPanel from "@/components/ReviewPanel";
import ChatPanel from "@/components/ChatPanel";
import EntityPanel from "@/components/EntityPanel";
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

  useEffect(() => {
    apiGet<Repository[]>("/api/repositories")
      .then((repos) => setRepo(repos.find((r) => r.id === repoId) ?? null))
      .catch((e) => setError(e.message));
  }, [repoId]);

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
      <div className="container">
        <div className="card" style={{ color: "var(--red)" }}>{error}</div>
      </div>
    );
  }

  return (
    <div className="container">
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", gap: 16, flexWrap: "wrap" }}>
        <div>
          <h1 style={{ marginBottom: 4 }}>{repo?.fullName ?? "…"}</h1>
          <div className="muted">
            {repo ? (
              <>
                {repo.nodeCount} nodes · {repo.edgeCount} edges · branch {repo.defaultBranch}
              </>
            ) : (
              "…"
            )}
          </div>
        </div>
        <SnapshotSelector snapshots={snapshots} commit={commit} onChange={setCommit} loading={snapshotsLoading} />
      </div>

      <div className="tabs" style={{ marginTop: 20 }}>
        <button className={`tab ${tab === "graph" ? "active" : ""}`} onClick={() => setTab("graph")}>Graph</button>
        <button className={`tab ${tab === "diff" ? "active" : ""}`} onClick={() => setTab("diff")}>Diff</button>
        <button className={`tab ${tab === "review" ? "active" : ""}`} onClick={() => setTab("review")}>Review</button>
        <button className={`tab ${tab === "chat" ? "active" : ""}`} onClick={() => setTab("chat")}>Chat</button>
      </div>

      <div style={{ display: "flex", gap: 16, alignItems: "stretch" }}>
        <div style={{ flex: 1, minWidth: 0 }}>
          {tab === "graph" && (
            <GraphView repoId={repoId} commit={commit} onSelect={openEntity} selectedKey={selectedKey} />
          )}
          {tab === "diff" && <DiffView repoId={repoId} snapshots={snapshots} onSelect={openEntity} />}
          {tab === "review" && <ReviewPanel repoId={repoId} onSelect={openEntity} />}
          {tab === "chat" && <ChatPanel repoId={repoId} commit={commit} onSelect={openEntity} />}
        </div>
        {selectedKey && (
          <div style={{ width: 420, flexShrink: 0 }}>
            <EntityPanel repoId={repoId} commit={commit} nodeKey={selectedKey} onClose={() => setSelectedKey(null)} />
          </div>
        )}
      </div>
    </div>
  );
}
