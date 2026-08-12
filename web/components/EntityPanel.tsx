"use client";

import { useCallback, useEffect, useState } from "react";
import ImpactPanel from "@/components/ImpactPanel";
import Mermaid from "@/components/Mermaid";
import Markdown from "@/components/Markdown";
import { apiGet } from "@/lib/api";
import type { Chain, ConsumerItem, Consumers, EdgeHistory, Graph, GraphNode } from "@/lib/types";
import { badge, badgeGreen, badgeOrange, badgeRed, badgeYellow, btn, btnSmall, card, path } from "@/lib/ui";

export default function EntityPanel({
  repoId,
  commit,
  nodeKey,
  onClose,
  onFocus,
  onOpenDiff,
}: {
  repoId: string;
  commit: string | null;
  nodeKey: string;
  onClose: () => void;
  onFocus: (key: string) => void;
  onOpenDiff?: (from: string, to: string) => void;
}) {
  const [node, setNode] = useState<GraphNode | null>(null);
  const [consumers, setConsumers] = useState<Consumers | null>(null);
  const [chain, setChain] = useState<Chain | null>(null);
  const [history, setHistory] = useState<EdgeHistory | null>(null);
  const [historyLoading, setHistoryLoading] = useState(false);
  const [historyError, setHistoryError] = useState<string | null>(null);
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

  const showHistory = useCallback((from: string, to: string) => {
    setHistoryLoading(true);
    setHistoryError(null);
    apiGet<EdgeHistory>(`/api/repositories/${repoId}/edge-history?from=${encodeURIComponent(from)}&to=${encodeURIComponent(to)}`)
      .then(setHistory)
      .catch((e) => setHistoryError(e.message))
      .finally(() => setHistoryLoading(false));
  }, [repoId]);

  const close = useCallback(onClose, [onClose]);

  return (
    <div className={`${card} w-full`} style={{ maxHeight: "calc(100vh - 220px)", overflow: "auto", position: "sticky", top: 20 }}>
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0">
          <strong>{node?.symbol ?? nodeKey}</strong>
          <div className={path}>{nodeKey}</div>
        </div>
        <button className={`${btn} ${btnSmall}`} type="button" onClick={close} aria-label="Close">✕</button>
      </div>

      {loading && <div className="mt-3 text-dim">Loading…</div>}
      {error && <div className="mt-3 text-danger">{error}</div>}

      {!loading && !error && !node && (
        <div className="mt-3 text-dim">Node not found in this snapshot.</div>
      )}

      {node && (
        <div className="mt-3">
          <div className="flex flex-wrap gap-2">
            <span className={`${badge} ${node.classification === "inference" ? badgeOrange : badgeGreen}`}>
              {node.classification === "inference"
                ? `inference · ${node.factSource ?? "Inference"}`
                : `fact · ${node.factSource ?? "AST"}`}
            </span>
            <span className={badge}>{node.kind}</span>
            <span className={badge}>{node.language}</span>
            <span className={`${badge} ${confidenceTone(node.confidence)}`}>confidence {node.confidence.toFixed(2)}</span>
            <span className={`${badge} ${tierTone(node.tier)}`}>{node.tier ?? "—"}</span>
            <span className={`${badge} ${reviewTone(node.reviewStatus)}`}>{node.reviewStatus.replace("_", " ")}</span>
          </div>
          <div className={`${path} mt-2`}>
            {node.path}:{node.line}–{node.endLine}
          </div>
          <div className="mt-3 rounded-lg border border-border bg-inset px-3 py-2.5 text-xs">
            <div className="mb-1.5 font-semibold text-dim">Provenance</div>
            <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-0.5">
              <dt className="text-dim">Classification</dt><dd>{node.classification ?? "—"}</dd>
              <dt className="text-dim">Source</dt><dd>{node.factSource ?? "—"}</dd>
              <dt className="text-dim">Tier</dt><dd>{node.tier ?? "—"}</dd>
              <dt className="text-dim">Confidence</dt><dd>{node.confidence.toFixed(2)}</dd>
              <dt className="text-dim">Commit</dt><dd className="font-mono">{node.commitSha?.slice(0, 10) || "—"}</dd>
              <dt className="text-dim">Model</dt><dd>{node.model ?? "—"}</dd>
              <dt className="text-dim">Prompt version</dt><dd>{node.promptVersion ?? "—"}</dd>
              <dt className="text-dim">Analyzed</dt><dd>{node.analyzedAt ? new Date(node.analyzedAt).toLocaleString() : "—"}</dd>
            </dl>
          </div>
          {node.content && (
            <div className="markdown mt-3 border-t border-border pt-3">
              <Markdown>{node.content}</Markdown>
            </div>
          )}
          {node.sequenceDiagram && (
            <div className="mt-3 border-t border-border pt-3">
              <div className="mb-1 font-semibold text-dim">Sequence diagram</div>
              <Mermaid chart={node.sequenceDiagram} />
            </div>
          )}
          {node.classDiagram && (
            <div className="mt-3 border-t border-border pt-3">
              <div className="mb-1 font-semibold text-dim">Class diagram</div>
              <Mermaid chart={node.classDiagram} />
            </div>
          )}
        </div>
      )}

      {consumers && (
        <div className="mt-4">
          <div className="font-semibold text-dim">Consumers ({consumers.items.length})</div>
          {consumers.items.length === 0 ? (
            <div className="text-dim">none</div>
          ) : (
            <ul className="mt-1 space-y-0.5">
              {consumers.items.map((c) => (
                <li key={`${c.fromKey}-${c.type}`}>
                  <button type="button" className="cursor-pointer text-accent hover:underline" onClick={() => onFocus(c.fromKey)}>
                    {c.fromSymbol}
                  </button>{" "}
                  <EdgeChip item={c} />{" "}
                  <span className={path}>{c.path}:{c.line}</span>{" "}
                  {c.evidence && <span className="font-mono text-xs text-dim">{c.evidence}</span>}{" "}
                  <button type="button" className="cursor-pointer text-xs text-dim hover:text-accent" onClick={() => showHistory(c.fromKey, nodeKey)}>
                    why?
                  </button>
                </li>
              ))}
            </ul>
          )}
        </div>
      )}

      {chain && (
        <div className="mt-4">
          <div className="font-semibold text-dim">Dependencies ({chain.items.length})</div>
          {chain.items.length === 0 ? (
            <div className="text-dim">none</div>
          ) : (
            <ul className="mt-1 space-y-0.5">
              {chain.items.map((c) => (
                <li key={c.key}>
                  <button type="button" className="cursor-pointer text-accent hover:underline" onClick={() => onFocus(c.key)}>
                    {c.symbol}
                  </button>{" "}
                  <EdgeChip item={c} />{" "}
                  <span className={path}>{c.path}:{c.line}</span>{" "}
                  {c.evidence && <span className="font-mono text-xs text-dim">{c.evidence}</span>}{" "}
                  <button type="button" className="cursor-pointer text-xs text-dim hover:text-accent" onClick={() => showHistory(nodeKey, c.key)}>
                    why?
                  </button>
                </li>
              ))}
            </ul>
          )}
        </div>
      )}

      {node && (
        <div className="mt-4">
          <ImpactPanel repoId={repoId} commit={commit} entityKey={node.key} onFocus={onFocus} />
        </div>
      )}

      {historyError && <div className="mt-4 text-danger">{historyError}</div>}
      {historyLoading && <div className="mt-4 text-dim">Loading history…</div>}
      {history && !historyLoading && (
        <div className="mt-4">
          <div className="font-semibold text-dim">
            Dependency history: <span className={path}>{history.from} → {history.to}</span>
          </div>
          {!history.exists && <div className="mt-1 text-xs text-warn">No longer exists at {history.commitSha.slice(0, 10)}.</div>}
          {history.entries.length === 0 ? (
            <div className="mt-1 text-dim">No recorded history for this dependency.</div>
          ) : (
            <ul className="mt-1 space-y-1">
              {history.entries.map((h, i) => (
                <li key={i} className="text-sm">
                  <span className="font-mono">{h.introducedCommit.slice(0, 10)}</span>{" "}
                  <span className="text-dim">{new Date(h.introducedAt).toLocaleDateString()}</span>{" "}
                  <span className="text-dim">({h.type}) · {h.ageInDays}d</span>
                  {onOpenDiff && h.introducedCommit !== history.commitSha && (
                    <button type="button" className="ml-1 cursor-pointer text-accent hover:underline" onClick={() => onOpenDiff(h.introducedCommit, history.commitSha)}>
                      diff →
                    </button>
                  )}
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
  return c < 0.7 ? badgeYellow : badgeGreen;
}

function tierTone(tier?: string) {
  switch (tier) {
    case "low-confidence":
      return badgeYellow;
    case "verified":
      return badgeGreen;
    default:
      return "";
  }
}

function EdgeChip({ item }: { item: { classification?: string; factSource?: string } }) {
  const isInference = item.classification === "inference";
  return (
    <span className={`${badge} ${isInference ? badgeOrange : badgeGreen}`}>
      {isInference ? `inference · ${item.factSource ?? "Inference"}` : "fact"}
    </span>
  );
}

function reviewTone(s: string) {
  switch (s) {
    case "needs_review":
    case "stale":
      return badgeRed;
    case "accepted":
    case "edited":
      return badgeGreen;
    default:
      return "";
  }
}
