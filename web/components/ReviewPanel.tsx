"use client";

import { useCallback, useEffect, useState } from "react";
import ReactMarkdown from "react-markdown";
import remarkGfm from "remark-gfm";
import { apiGet, apiPost } from "@/lib/api";
import type { ReviewItem, ReviewList } from "@/lib/types";
import { badge, badgeOrange, badgeYellow, btn, btnDanger, btnPrimary, btnSmall, card, field, path } from "@/lib/ui";

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

  if (loading && !review) return <div className={card}>Loading review queue…</div>;
  if (error && !review) return <div className={`${card} text-danger`}>{error}</div>;
  if (!review) return null;

  return (
    <div>
      <div className="mb-3 text-dim">
        Review queue for commit <code>{review.commitSha.slice(0, 10)}</code> — {review.items.length} node(s) flagged.
      </div>
      {error && <div className="mb-3 text-danger">{error}</div>}
      {review.items.length === 0 && <div className={card}>Queue is empty. All nodes reviewed.</div>}
      {review.items.map((item) => (
        <div key={item.nodeId} className={`${card} mb-3`}>
          <div className="flex items-center justify-between gap-3">
            <div className="min-w-0">
              <strong className="cursor-pointer text-fg hover:text-accent" onClick={() => onSelect(item.key)}>{item.symbol}</strong>
              <span className={path}> {item.key}</span>
              <div className="text-xs text-dim">{item.path}:{item.line}–{item.endLine}</div>
            </div>
            <span className={`${badge} ${item.confidence < 0.7 ? badgeYellow : badgeOrange}`}>confidence {item.confidence.toFixed(2)}</span>
          </div>

          <div className="markdown mt-2.5">
            {editingId === item.nodeId ? (
              <textarea
                value={editingContent}
                onChange={(e) => setEditingContent(e.target.value)}
                rows={10}
                className={`${field} font-mono`}
              />
            ) : (
              <ReactMarkdown remarkPlugins={[remarkGfm]}>{item.content}</ReactMarkdown>
            )}
          </div>

          <div className="mt-2.5 flex gap-2">
            {editingId === item.nodeId ? (
              <>
                <button className={`${btn} ${btnPrimary} ${btnSmall}`} disabled={busyId === item.nodeId} onClick={() => act(item.nodeId, "edit")}>
                  Save version
                </button>
                <button className={`${btn} ${btnSmall}`} onClick={() => setEditingId(null)}>Cancel</button>
              </>
            ) : (
              <>
                <button className={`${btn} ${btnPrimary} ${btnSmall}`} disabled={busyId === item.nodeId} onClick={() => act(item.nodeId, "accept")}>
                  Accept
                </button>
                <button className={`${btn} ${btnSmall}`} disabled={busyId === item.nodeId} onClick={() => { setEditingContent(item.content); setEditingId(item.nodeId); }}>
                  Edit
                </button>
                <button className={`${btn} ${btnDanger} ${btnSmall}`} disabled={busyId === item.nodeId} onClick={() => act(item.nodeId, "dismiss")}>
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
