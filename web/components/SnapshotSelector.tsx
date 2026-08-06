"use client";

import type { Snapshot } from "@/lib/types";

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
      <span className="badge">
        <span className="spinner" /> loading snapshots…
      </span>
    );
  }
  return (
    <label style={{ display: "flex", alignItems: "center", gap: 8 }}>
      <span className="muted">Snapshot:</span>
      <select
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
