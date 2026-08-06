"use client";

import { useCallback, useEffect, useState } from "react";
import ReactMarkdown from "react-markdown";
import remarkGfm from "remark-gfm";
import { apiGet, apiPost } from "@/lib/api";
import type { ReviewItem, ReviewList } from "@/lib/types";

export default function ReviewPanel({
  repoId,
  onSelect,
}: {
  repoId: string;
  onSelect: (key: string) => void;
}) {
  const [review, setReview] = useState<ReviewList | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [editingContent, setEditingContent] = useState("");
  const [busyId, setBusyId] = useState<string | null>(null);

  const load = useCallback(() => {
    setLoading(true);
    apiGet<ReviewList>(`/api/repositories/${repoId}/review`)
      .then(setReview)
      .catch((e) => setError(e.message))
      .finally(() => setLoading(false));
  }, [repoId]);

  useEffect(() => {
    load();
  }, [load]);

  const act = async (id: string, action: "accept" | "dismiss" | "edit") => {
    setBusyId(id);
    setError(null);
    try {
      const body = action === "edit" ? { content: editingContent, editedBy: "dashboard" } : undefined;
      await apiPost(`/api/repositories/${repoId}/review/${id}/${action}`, body);
      setEditingId(null);
      await load();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusyId(null);
    }
  };

  if (loading && !review) return <div className="panel muted">Loading review queue…</div>;
  if (error && !review) return <div className="panel" style={{ color: "var(--red)" }}>{error}</div>;
  if (!review) return null;

  return (
    <div>
      <div className="muted" style={{ marginBottom: 12 }}>
        Review queue for commit <code>{review.commitSha.slice(0, 10)}</code> — {review.items.length} node(s) flagged.
      </div>
      {error && <div style={{ color: "var(--red)", marginBottom: 12 }}>{error}</div>}
      {review.items.length === 0 && <div className="panel muted">Queue is empty. All nodes reviewed.</div>}
      {review.items.map((item) => (
        <div key={item.nodeId} className="card" style={{ marginBottom: 12 }}>
          <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", gap: 8 }}>
            <div style={{ minWidth: 0 }}>
              <strong style={{ cursor: "pointer" }} onClick={() => onSelect(item.key)}>{item.symbol}</strong>
              <span className="path"> {item.key}</span>
              <div className="muted" style={{ fontSize: 12 }}>{item.path}:{item.line}–{item.endLine}</div>
            </div>
            <span className={`badge ${item.confidence < 0.7 ? "yellow" : "orange"}`}>confidence {item.confidence.toFixed(2)}</span>
          </div>

          <div className="markdown" style={{ marginTop: 10 }}>
            {editingId === item.nodeId ? (
              <textarea
                value={editingContent}
                onChange={(e) => setEditingContent(e.target.value)}
                rows={10}
                style={{ width: "100%" }}
              />
            ) : (
              <ReactMarkdown remarkPlugins={[remarkGfm]}>{item.content}</ReactMarkdown>
            )}
          </div>

          <div style={{ display: "flex", gap: 8, marginTop: 10 }}>
            {editingId === item.nodeId ? (
              <>
                <button className="btn primary small" disabled={busyId === item.nodeId} onClick={() => act(item.nodeId, "edit")}>
                  Save version
                </button>
                <button className="btn small" onClick={() => setEditingId(null)}>Cancel</button>
              </>
            ) : (
              <>
                <button className="btn primary small" disabled={busyId === item.nodeId} onClick={() => act(item.nodeId, "accept")}>
                  Accept
                </button>
                <button className="btn small" disabled={busyId === item.nodeId} onClick={() => { setEditingContent(item.content); setEditingId(item.nodeId); }}>
                  Edit
                </button>
                <button className="btn danger small" disabled={busyId === item.nodeId} onClick={() => act(item.nodeId, "dismiss")}>
                  Dismiss
                </button>
              </>
            )}
          </div>
        </div>
      ))}
    </div>
  );
}
