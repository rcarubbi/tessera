"use client";

import { useEffect, useMemo, useState } from "react";
import { apiGet } from "@/lib/api";
import type { Diff, Snapshot } from "@/lib/types";

export default function DiffView({
  repoId,
  snapshots,
  onSelect,
}: {
  repoId: string;
  snapshots: Snapshot[];
  onSelect: (key: string) => void;
}) {
  const sorted = useMemo(() => [...snapshots].sort((a, b) => b.createdAt.localeCompare(a.createdAt)), [snapshots]);
  const [from, setFrom] = useState<string>("");
  const [to, setTo] = useState<string>("");
  const [diff, setDiff] = useState<Diff | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (sorted.length >= 2) {
      setFrom((f) => f || sorted[sorted.length - 1].commitSha);
      setTo((t) => t || sorted[0].commitSha);
    }
  }, [sorted]);

  const run = () => {
    if (!from || !to) return;
    setLoading(true);
    setError(null);
    apiGet<Diff>(`/api/repositories/${repoId}/diff?from=${from}&to=${to}`)
      .then(setDiff)
      .catch((e) => setError(e.message))
      .finally(() => setLoading(false));
  };

  useEffect(() => {
    if (from && to) run();
  }, [from, to]);

  if (sorted.length === 0) {
    return <div className="panel muted">No snapshots to compare.</div>;
  }

  const added = diff?.nodes.filter((n) => n.change === "added") ?? [];
  const removed = diff?.nodes.filter((n) => n.change === "removed") ?? [];
  const changed = diff?.nodes.filter((n) => n.change === "changed") ?? [];

  return (
    <div>
      <div className="card" style={{ marginBottom: 12, display: "flex", gap: 12, alignItems: "center", flexWrap: "wrap" }}>
        <label style={{ display: "flex", alignItems: "center", gap: 6 }}>
          <span className="muted">From:</span>
          <select value={from} onChange={(e) => setFrom(e.target.value)}>
            {sorted.map((s) => (
              <option key={s.id} value={s.commitSha}>{s.commitSha.slice(0, 10)}</option>
            ))}
          </select>
        </label>
        <label style={{ display: "flex", alignItems: "center", gap: 6 }}>
          <span className="muted">To:</span>
          <select value={to} onChange={(e) => setTo(e.target.value)}>
            {sorted.map((s) => (
              <option key={s.id} value={s.commitSha}>{s.commitSha.slice(0, 10)}</option>
            ))}
          </select>
        </label>
        {loading && <span className="spinner" />}
      </div>

      {error && <div className="panel mb-3 text-danger">{error}</div>}

      {diff && (
        <>
          {(diff.cycles?.length ?? 0) > 0 && (
            <div style={{ marginBottom: 12 }}>
              <div className="muted" style={{ fontWeight: 600, marginBottom: 6 }}>New dependency cycles ({diff.cycles!.length})</div>
              {diff.cycles!.map((c, i) => (
                <div key={i} className="cycle-banner" style={{ marginBottom: 6 }}>
                  {c.path.map((k, j) => (
                    <span key={k}>
                      <button className="btn small" style={{ background: "transparent" }} onClick={() => onSelect(k)}>{k}</button>
                      {j < c.path.length - 1 && " → "}
                    </span>
                  ))}
                </div>
              ))}
            </div>
          )}

          <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 12 }}>
            <div className="panel">
              <div className="diff-added" style={{ fontWeight: 600 }}>Added ({added.length})</div>
              <ChangeList items={added} onSelect={onSelect} tone="added" />
            </div>
            <div className="panel">
              <div className="diff-removed" style={{ fontWeight: 600 }}>Removed ({removed.length})</div>
              <ChangeList items={removed} onSelect={onSelect} tone="removed" />
            </div>
            <div className="panel">
              <div className="diff-changed" style={{ fontWeight: 600 }}>Changed ({changed.length})</div>
              <ChangeList items={changed} onSelect={onSelect} tone="changed" />
            </div>
            <div className="panel">
              <div style={{ fontWeight: 600 }} className="muted">Edges ({diff.edges.length})</div>
              <ul className="list" style={{ marginTop: 6 }}>
                {diff.edges.map((e, i) => (
                  <li key={i} style={{ cursor: "default" }}>
                    <span className={e.change === "added" ? "diff-added" : "diff-removed"}>
                      {e.change === "added" ? "+" : "−"} {e.from} → {e.to}
                    </span>{" "}
                    <span className="muted">({e.type})</span>
                  </li>
                ))}
              </ul>
            </div>
          </div>
        </>
      )}
    </div>
  );
}

function ChangeList({ items, onSelect, tone }: { items: { key: string; symbol: string }[]; onSelect: (k: string) => void; tone: string }) {
  if (items.length === 0) return <div className="muted">none</div>;
  return (
    <ul className="list" style={{ marginTop: 6 }}>
      {items.map((n) => (
        <li key={`${tone}-${n.key}`} onClick={() => onSelect(n.key)}>
          <span className={`diff-${tone}`}>{n.symbol}</span> <span className="path">{n.key}</span>
        </li>
      ))}
    </ul>
  );
}
