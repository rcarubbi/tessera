"use client";

import { useEffect, useState } from "react";
import { badge, card, field } from "@/lib/ui";

export type NodeHit = {
  key: string;
  symbol: string;
  kind: string;
  language: string;
  path: string;
  startLine: number;
  endLine: number;
  confidence: number;
  reviewStatus: string;
};

export default function FilesPanel({
  repoId,
  commit,
  onSelect,
}: {
  repoId: string;
  commit: string | null;
  onSelect: (key: string) => void;
}) {
  const [query, setQuery] = useState("");
  const [results, setResults] = useState<NodeHit[] | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const q = query.trim();
    if (!q) {
      setResults(null);
      return;
    }
    setLoading(true);
    setError(null);
    let cancelled = false;
    const controller = new AbortController();
    const commitParam = commit ? `&commit=${encodeURIComponent(commit)}` : "";
    import("@/lib/api")
      .then(({ apiGet }) =>
        apiGet<NodeHit[]>(
          `/api/repositories/${repoId}/nodes?q=${encodeURIComponent(q)}&limit=100${commitParam}`,
          controller.signal,
        ),
      )
      .then((items) => {
        if (!cancelled) setResults(items);
      })
      .catch((e) => {
        if (!cancelled && (e as Error).name !== "AbortError") setError((e as Error).message);
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
      controller.abort();
    };
  }, [repoId, commit, query]);

  const groups = useGroups(results ?? []);

  return (
    <div className={card}>
      <div className="mb-4">
        <h2 className="text-lg font-bold">File search</h2>
        <p className="text-sm text-dim">
          Search analyzed files and symbols. Click a result to inspect it in the graph.
        </p>
      </div>
      <input
        value={query}
        onChange={(e) => setQuery(e.target.value)}
        placeholder="Search files, symbols, paths…"
        className={`${field} max-w-md`}
        autoFocus
      />
      {error && <div className="mt-3 text-danger">{error}</div>}
      {loading && <div className="mt-3 text-dim">Searching…</div>}
      {query.trim() && !loading && !error && results && results.length === 0 && (
        <div className="mt-3 text-dim">No matches for &quot;{query}&quot;.</div>
      )}
      {query.trim() && !loading && results && results.length > 0 && (
        <div className="mt-3 text-xs text-dim">{results.length} matches</div>
      )}
      <div className="mt-2">
        {Object.entries(groups).map(([path, items]) => (
          <div key={path} className="mb-2">
            <div className="truncate font-mono text-xs text-dim">{path}</div>
            <div className="ml-3">
              {items.map((n) => (
                <button
                  key={n.key}
                  type="button"
                  className="flex w-full items-center gap-2 rounded px-1.5 py-1 text-left text-sm hover:bg-inset"
                  onClick={() => onSelect(n.key)}
                >
                  <span className={badge}>{n.kind}</span>
                  <span className="truncate text-fg">{n.symbol}</span>
                  <span className="ml-auto shrink-0 text-xs text-dim">line {n.startLine}</span>
                </button>
              ))}
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}

function useGroups(items: NodeHit[]): Record<string, NodeHit[]> {
  const groups: Record<string, NodeHit[]> = {};
  for (const n of items) {
    (groups[n.path] ??= []).push(n);
  }
  return groups;
}
