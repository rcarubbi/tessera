"use client";

import type { Snapshot } from "@/lib/types";
import { badge, spinner } from "@/lib/ui";

export default function SnapshotSelector({
  snapshots,
  commit,
  onChange,
  loading,
}: {
  snapshots: Snapshot[];
  commit: string | null;
  onChange: (commit: string | null) => void;
  loading: boolean;
}) {
  if (loading) {
    return (
      <span className={badge}>
        <span className={spinner} /> loading snapshots…
      </span>
    );
  }
  return (
    <label className="flex items-center gap-2">
      <span className="text-dim">Snapshot:</span>
      <select
        className="rounded-lg border border-border bg-inset px-3 py-2 text-sm text-fg outline-none focus:border-accent"
        value={commit ?? ""}
        onChange={(e) => onChange(e.target.value === "" ? null : e.target.value)}
      >
        <option value="">Latest</option>
        {snapshots.map((s) => (
          <option key={s.id} value={s.commitSha}>
            {s.commitSha.slice(0, 10)} · {s.nodeCount} nodes
          </option>
        ))}
      </select>
    </label>
  );
}
