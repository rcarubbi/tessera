"use client";

import { useEffect, useMemo, useState } from "react";
import { apiGet } from "@/lib/api";
import type { Diff, Snapshot } from "@/lib/types";
import { card, cardError, path, select, spinner } from "@/lib/ui";

export default function DiffView({
  repoId,
  snapshots,
  onSelect,
  initialFrom,
  initialTo,
}: {
  repoId: string;
  snapshots: Snapshot[];
  onSelect: (key: string) => void;
  initialFrom?: string;
  initialTo?: string;
}) {
  const sorted = useMemo(() => [...snapshots].sort((a, b) => b.createdAt.localeCompare(a.createdAt)), [snapshots]);
  const [from, setFrom] = useState<string>("");
  const [to, setTo] = useState<string>("");
  const [diff, setDiff] = useState<Diff | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (initialFrom) setFrom(initialFrom);
    if (initialTo) setTo(initialTo);
  }, [initialFrom, initialTo]);

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
    return <div className={card}>No snapshots to compare.</div>;
  }

  const added = diff?.nodes.filter((n) => n.change === "added") ?? [];
  const removed = diff?.nodes.filter((n) => n.change === "removed") ?? [];
  const changed = diff?.nodes.filter((n) => n.change === "changed") ?? [];

  return (
    <div>
      <div className={`${card} mb-3 flex flex-wrap items-center gap-3`}>
        <label className="flex items-center gap-1.5 text-sm">
          <span className="text-dim">From:</span>
          <select className={select} value={from} onChange={(e) => setFrom(e.target.value)}>
            {sorted.map((s) => (
              <option key={s.id} value={s.commitSha}>{s.commitSha.slice(0, 10)}</option>
            ))}
          </select>
        </label>
        <label className="flex items-center gap-1.5 text-sm">
          <span className="text-dim">To:</span>
          <select className={select} value={to} onChange={(e) => setTo(e.target.value)}>
            {sorted.map((s) => (
              <option key={s.id} value={s.commitSha}>{s.commitSha.slice(0, 10)}</option>
            ))}
          </select>
        </label>
        {loading && <span className={spinner} />}
      </div>

      {error && <div className={`${card} ${cardError} mb-3 text-danger`}>{error}</div>}

      {diff && (
        <>
          {(diff.cycles?.length ?? 0) > 0 && (
            <div className="mb-3">
              <div className="mb-1.5 font-semibold text-dim">
                New dependency cycles ({diff.cycles!.length})
              </div>
              {diff.cycles!.map((c, i) => (
                <div key={i} className="mb-1.5 flex flex-wrap items-center gap-1 rounded-lg border border-danger bg-danger/10 px-3 py-2 font-mono text-sm text-danger">
                  {c.path.map((k, j) => (
                    <span key={k} className="flex items-center gap-1">
                      <button
                        type="button"
                        className="cursor-pointer font-medium text-danger hover:underline"
                        onClick={() => onSelect(k)}
                      >
                        {k}
                      </button>
                      {j < c.path.length - 1 && <span className="text-danger/60">→</span>}
                    </span>
                  ))}
                </div>
              ))}
            </div>
          )}

          <div className="grid grid-cols-1 gap-3 md:grid-cols-2">
            <div className={card}>
              <div className="mb-2 font-semibold text-good">Added ({added.length})</div>
              <ChangeList items={added} onSelect={onSelect} tone="added" />
            </div>
            <div className={card}>
              <div className="mb-2 font-semibold text-danger">Removed ({removed.length})</div>
              <ChangeList items={removed} onSelect={onSelect} tone="removed" />
            </div>
            <div className={card}>
              <div className="mb-2 font-semibold text-warn">Changed ({changed.length})</div>
              <ChangeList items={changed} onSelect={onSelect} tone="changed" />
            </div>
            <div className={card}>
              <div className="mb-2 font-semibold text-dim">Edges ({diff.edges.length})</div>
              <ul className="space-y-0.5">
                {diff.edges.map((e, i) => (
                  <li key={i} className="text-sm">
                    <span className={e.change === "added" ? "text-good" : "text-danger"}>
                      {e.change === "added" ? "+" : "−"} {e.from} → {e.to}
                    </span>{" "}
                    <span className="text-dim">({e.type})</span>                  </li>
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
  if (items.length === 0) return <div className="text-dim">none</div>;
  return (
    <ul className="space-y-0.5">
      {items.map((n) => (
        <li key={`${tone}-${n.key}`} className="text-sm">
          <button
            type="button"
            className={`cursor-pointer hover:underline ${tone === "added" ? "text-good" : tone === "removed" ? "text-danger" : "text-warn"}`}
            onClick={() => onSelect(n.key)}
          >
            {n.symbol}
          </button>{" "}
          <span className={path}>{n.key}</span>
        </li>
      ))}
    </ul>
  );
}
