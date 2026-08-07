"use client";

import { useCallback, useEffect, useState } from "react";
import Mermaid from "@/components/Mermaid";
import Markdown from "@/components/Markdown";
import { apiGet } from "@/lib/api";
import type { Chain, Consumers, Graph, GraphNode } from "@/lib/types";

export default function EntityPanel({
  repoId,
  commit,
  nodeKey,
  onClose,
  onFocus,
}: {
  repoId: string;
  commit: string | null;
  nodeKey: string;
  onClose: () => void;
  onFocus: (key: string) => void;
}) {
  const [node, setNode] = useState<GraphNode | null>(null);
  const [consumers, setConsumers] = useState<Consumers | null>(null);
  const [chain, setChain] = useState<Chain | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const commitParam = commit ? `&commit=${encodeURIComponent(commit)}` : "";

  useEffect(() => {
    setLoading(true);
    setError(null);
    const suffix = `&entity=${encodeURIComponent(nodeKey)}&maxDepth=1${commitParam}`;
    Promise.all([
      apiGet<Graph>(`/api/repositories/${repoId}/graph?${suffix}`),
      apiGet<Consumers>(`/api/repositories/${repoId}/consumers?entity=${encodeURIComponent(nodeKey)}${commitParam}`),
      apiGet<Chain>(`/api/repositories/${repoId}/chain?entity=${encodeURIComponent(nodeKey)}${commitParam}`),
    ])
      .then(([graph, cons, ch]) => {
        setNode(graph.nodes.find((n) => n.key === nodeKey) ?? null);
        setConsumers(cons);
        setChain(ch);
      })
      .catch((e) => setError(e.message))
      .finally(() => setLoading(false));
  }, [repoId, commit, nodeKey, commitParam]);

  const close = useCallback(onClose, [onClose]);

  return (
    <div className="panel" style={{ maxHeight: "calc(100vh - 220px)", overflow: "auto", position: "sticky", top: 20 }}>
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
        <div>
          <strong>{node?.symbol ?? nodeKey}</strong>
          <div className="muted" style={{ fontFamily: "monospace", fontSize: 12 }}>{nodeKey}</div>
        </div>
        <button className="btn small" onClick={close}>✕</button>
      </div>

      {loading && <div className="muted" style={{ marginTop: 12 }}>Loading…</div>}
      {error && <div className="mt-3 text-danger">{error}</div>}

      {!loading && !error && !node && (
        <div className="mt-3 text-dim">Node not found in this snapshot.</div>
      )}

      {node && (
        <div style={{ marginTop: 12 }}>
          <div style={{ display: "flex", gap: 6, flexWrap: "wrap" }}>
            <span className="badge">{node.kind}</span>
            <span className="badge">{node.language}</span>
            <span className={`badge ${confidenceTone(node.confidence)}`}>confidence {node.confidence.toFixed(2)}</span>
            <span className={`badge ${reviewTone(node.reviewStatus)}`}>{node.reviewStatus.replace("_", " ")}</span>
          </div>
          <div className="muted" style={{ fontFamily: "monospace", fontSize: 12, marginTop: 8 }}>
            {node.path}:{node.line}–{node.endLine}
          </div>
          {node.content && (
            <div className="markdown mt-3 border-t border-border pt-3">
              <Markdown>{node.content}</Markdown>
            </div>
          )}
          {node.sequenceDiagram && (
            <div className="mt-3 border-t border-border pt-3">
              <div className="muted" style={{ fontWeight: 600, marginBottom: 4 }}>
                Sequence diagram
              </div>
              <Mermaid chart={node.sequenceDiagram} />
            </div>
          )}
          {node.classDiagram && (
            <div className="mt-3 border-t border-border pt-3">
              <div className="muted" style={{ fontWeight: 600, marginBottom: 4 }}>
                Class diagram
              </div>
              <Mermaid chart={node.classDiagram} />
            </div>
          )}
        </div>
      )}

      {consumers && (
        <div style={{ marginTop: 16 }}>
          <div className="muted" style={{ fontWeight: 600 }}>Consumers ({consumers.items.length})</div>
          {consumers.items.length === 0 ? (
            <div className="muted">none</div>
          ) : (
            <ul className="list" style={{ marginTop: 4 }}>
              {consumers.items.map((c) => (
                <li key={`${c.fromKey}-${c.type}`}>
                  <button type="button" className="link-button" onClick={() => onFocus(c.fromKey)}>
                    {c.fromSymbol}
                  </button>{" "}
                  <span className="path">{c.path}:{c.line}</span>
                </li>
              ))}
            </ul>
          )}
        </div>
      )}

      {chain && (
        <div style={{ marginTop: 16 }}>
          <div className="muted" style={{ fontWeight: 600 }}>Dependencies ({chain.items.length})</div>
          {chain.items.length === 0 ? (
            <div className="muted">none</div>
          ) : (
            <ul className="list" style={{ marginTop: 4 }}>
              {chain.items.map((c) => (
                <li key={c.key}>
                  <button type="button" className="link-button" onClick={() => onFocus(c.key)}>
                    {c.symbol}
                  </button>{" "}
                  <span className="path">{c.path}:{c.line}</span>
                </li>
              ))}
            </ul>
          )}
        </div>
      )}
    </div>
  );
}

function confidenceTone(c: number) {
  return c < 0.7 ? "badge-yellow" : "badge-green";
}

function reviewTone(s: string) {
  switch (s) {
    case "needs_review":
    case "stale":
      return "badge-red";
    case "accepted":
    case "edited":
      return "badge-green";
    default:
      return "";
  }
}
